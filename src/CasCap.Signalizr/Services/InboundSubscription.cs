using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace CasCap.Services;

/// <summary>One connected subscriber's durable replay cursor and acknowledgement budget.</summary>
public sealed class InboundSubscription : IDisposable
{
    private readonly IDbContextFactory<SignalizrDbContext> _dbContextFactory;
    private readonly Channel<bool> _notifications;
    private readonly Queue<(long Id, long ReservedAt)> _outstanding = new();
    private readonly Channel<bool> _acknowledgementChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Lock _outstandingLock = new();
    private readonly SemaphoreSlim _budget;
    private readonly SemaphoreSlim _cursorLock = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly SignalizrMetrics _metrics;
    private readonly string[] _groups;
    private readonly int _replayBatchSize;
    private TimeSpan? _acknowledgementTimeout;
    private long _nextMessageId;
    // Highest id below which every message is delivered or not for this subscriber.
    private long _scannedThrough;
    private long _persistedCursor;

    internal InboundSubscription(
        string name,
        string[] groups,
        int replayBatchSize,
        int maxOutstanding,
        long lastAcknowledgedMessageId,
        TimeProvider timeProvider,
        SignalizrMetrics metrics,
        IDbContextFactory<SignalizrDbContext> dbContextFactory)
    {
        Name = name;
        _groups = groups;
        _replayBatchSize = replayBatchSize;
        _nextMessageId = lastAcknowledgedMessageId;
        _scannedThrough = lastAcknowledgedMessageId;
        _persistedCursor = lastAcknowledgedMessageId;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _dbContextFactory = dbContextFactory;
        _budget = new SemaphoreSlim(maxOutstanding, maxOutstanding);
        _notifications = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Stable identity used as the durable cursor key.</summary>
    public string Name { get; }

    /// <summary>Reads persisted messages for this subscriber's groups after its last acknowledged sequence.</summary>
    public async IAsyncEnumerable<InboundDelivery> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            List<InboundMessageEntity> messages;
            long scanTail;
            await using (var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                // Bounding the scan first means every unmatched id up to the tail is known to be skippable.
                var tail = await dbContext.InboundMessages
                    .Where(message => message.Id > _nextMessageId)
                    .MaxAsync(message => (long?)message.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (tail is null)
                {
                    await _notifications.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                scanTail = tail.Value;
                messages = await dbContext.InboundMessages
                    .AsNoTracking()
                    .Include(message => message.Attachments)
                    .Where(message => message.Id > _nextMessageId
                        && message.Id <= scanTail
                        && message.GroupName != null
                        && _groups.Contains(message.GroupName))
                    .OrderBy(message => message.Id)
                    .Take(_replayBatchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            foreach (var message in messages)
            {
                _nextMessageId = message.Id;
                yield return ToDelivery(message);
            }

            // A full batch may leave matching messages below the tail; read them before skipping ahead.
            if (messages.Count < _replayBatchSize)
            {
                _nextMessageId = scanTail;
                await SkipThroughAsync(scanTail, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static InboundDelivery ToDelivery(InboundMessageEntity message) => new()
    {
        DeliveryId = message.Id.ToString(CultureInfo.InvariantCulture),
        GroupName = message.GroupName,
        Sender = message.Sender,
        Message = message.Message,
        Timestamp = message.Timestamp,
        FromSelf = message.FromSelf,
        PollVote = message.PollVoteTimestamp is { } pollTimestamp
            ? new InboundPollVote
            {
                PollTimestamp = pollTimestamp,
                OptionIndexes = InboundPollVote.ParseOptionIndexes(message.PollVoteOptionIndexes)
            }
            : null,
        Attachments = [.. message.Attachments.Select(attachment => new InboundAttachment
        {
            Id = attachment.Id,
            ContentType = attachment.ContentType,
            Filename = attachment.Filename,
            Size = attachment.Content.LongLength
        })]
    };

    /// <summary>Records that nothing up to <paramref name="messageId"/> is still to be delivered.</summary>
    /// <remarks>
    /// The cursor moves only when nothing is outstanding; otherwise the acknowledgement that empties
    /// the outstanding queue moves it, so an undelivered or unacknowledged message is never passed.
    /// </remarks>
    private async Task SkipThroughAsync(long messageId, CancellationToken cancellationToken)
    {
        bool idle;
        lock (_outstandingLock)
        {
            _scannedThrough = Math.Max(_scannedThrough, messageId);
            idle = _outstanding.Count == 0;
        }

        if (idle)
            await PersistCursorAsync(messageId, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistCursorAsync(long messageId, CancellationToken cancellationToken)
    {
        await _cursorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (messageId <= _persistedCursor)
                return;

            await using var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var cursor = await dbContext.SubscriberCursors
                .SingleAsync(candidate => candidate.SubscriberName == Name, cancellationToken)
                .ConfigureAwait(false);
            cursor.LastAcknowledgedMessageId = messageId;
            cursor.UpdatedAtUtc = _timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _persistedCursor = messageId;
        }
        finally
        {
            _cursorLock.Release();
        }
    }

    /// <summary>Signals that persisted messages may be available.</summary>
    public void NotifyMessageAvailable() => _notifications.Writer.TryWrite(true);

    /// <summary>
    /// Takes one unit of the outstanding-acknowledgement budget, waiting when it is exhausted.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the budget did not free up in time, meaning the subscriber is
    /// receiving but not acknowledging.
    /// </returns>
    public async Task<bool> TryReserveAsync(
        string deliveryId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(deliveryId, NumberStyles.None, CultureInfo.InvariantCulture, out var messageId)
            || !await _budget.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            return false;

        lock (_outstandingLock)
            _outstanding.Enqueue((messageId, _timeProvider.GetTimestamp()));

        _acknowledgementChanges.Writer.TryWrite(true);
        return true;
    }

    /// <summary>Waits until the oldest reservation expires, even when no new messages arrive.</summary>
    /// <remarks>
    /// One stream-owned monitor sets the maximum reservation age independently of admission waits.
    /// It must be cancelled and awaited before disposing this subscription.
    /// </remarks>
    public async Task WaitForAcknowledgementTimeoutAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        lock (_outstandingLock)
            _acknowledgementTimeout = timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan? remaining = null;
            lock (_outstandingLock)
            {
                if (_outstanding.TryPeek(out var oldest))
                {
                    remaining = timeout - _timeProvider.GetElapsedTime(oldest.ReservedAt);
                    if (remaining <= TimeSpan.Zero)
                        return;
                }
            }

            if (remaining is null)
            {
                await _acknowledgementChanges.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            using var expiry = new CancellationTokenSource(remaining.Value, _timeProvider);
            using var wakeup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);
            try
            {
                await _acknowledgementChanges.Reader.ReadAsync(wakeup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // Recheck under the lock: a timely acknowledgement may have changed the head.
            }
        }
    }

    /// <summary>Advances the durable cursor when the oldest outstanding delivery is acknowledged.</summary>
    public async Task<bool> AcknowledgeAsync(
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(deliveryId, NumberStyles.None, CultureInfo.InvariantCulture, out var messageId))
            return false;

        lock (_outstandingLock)
        {
            if (!_outstanding.TryPeek(out var oldest) || oldest.Id != messageId
                || (_acknowledgementTimeout is { } timeout
                    && _timeProvider.GetElapsedTime(oldest.ReservedAt) >= timeout))
                return false;
        }

        await PersistCursorAsync(messageId, cancellationToken).ConfigureAwait(false);
        _metrics.RecordAcknowledged();

        long skippedThrough = 0;
        lock (_outstandingLock)
        {
            _outstanding.Dequeue();
            if (_outstanding.Count == 0)
                skippedThrough = _scannedThrough;
        }

        _budget.Release();
        _acknowledgementChanges.Writer.TryWrite(true);
        if (skippedThrough > messageId)
            await PersistCursorAsync(skippedThrough, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Ends the subscription and wakes its reader.</summary>
    public void Complete(Exception? error = null) => _notifications.Writer.TryComplete(error);

    /// <inheritdoc/>
    public void Dispose()
    {
        Complete();
        _budget.Dispose();
        _cursorLock.Dispose();
    }
}
