using CasCap.Data;
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
    private readonly Queue<long> _outstanding = new();
    private readonly Lock _outstandingLock = new();
    private readonly SemaphoreSlim _budget;
    private readonly TimeProvider _timeProvider;
    private readonly SignalizrMetrics _metrics;
    private readonly int _replayBatchSize;
    private long _nextMessageId;

    internal InboundSubscription(
        string name,
        int replayBatchSize,
        int maxOutstanding,
        long lastAcknowledgedMessageId,
        TimeProvider timeProvider,
        SignalizrMetrics metrics,
        IDbContextFactory<SignalizrDbContext> dbContextFactory)
    {
        Name = name;
        _replayBatchSize = replayBatchSize;
        _nextMessageId = lastAcknowledgedMessageId;
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

    /// <summary>Reads persisted messages after this subscriber's last acknowledged sequence.</summary>
    public async IAsyncEnumerable<InboundDelivery> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var dbContext = await _dbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);
            var messages = await dbContext.InboundMessages
                .AsNoTracking()
                .Include(message => message.Attachments)
                .Where(message => message.Id > _nextMessageId)
                .OrderBy(message => message.Id)
                .Take(_replayBatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (messages.Count == 0)
            {
                await _notifications.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var message in messages)
            {
                _nextMessageId = message.Id;
                yield return new InboundDelivery
                {
                    DeliveryId = message.Id.ToString(CultureInfo.InvariantCulture),
                    Channel = message.Channel,
                    Sender = message.Sender,
                    Message = message.Message,
                    Timestamp = message.Timestamp,
                    Attachments = [.. message.Attachments.Select(attachment => new InboundAttachment
                    {
                        Id = attachment.Id,
                        ContentType = attachment.ContentType,
                        Filename = attachment.Filename,
                        Size = attachment.Content.LongLength
                    })]
                };
            }
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
            _outstanding.Enqueue(messageId);

        return true;
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
            if (_outstanding.Count == 0 || _outstanding.Peek() != messageId)
                return false;
        }

        await using var dbContext = await _dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        var cursor = await dbContext.SubscriberCursors
            .SingleAsync(candidate => candidate.SubscriberName == Name, cancellationToken)
            .ConfigureAwait(false);
        cursor.LastAcknowledgedMessageId = messageId;
        cursor.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _metrics.RecordAcknowledged();

        lock (_outstandingLock)
            _outstanding.Dequeue();

        _budget.Release();
        return true;
    }

    /// <summary>Ends the subscription and wakes its reader.</summary>
    public void Complete(Exception? error = null) => _notifications.Writer.TryComplete(error);

    /// <inheritdoc/>
    public void Dispose()
    {
        Complete();
        _budget.Dispose();
    }
}
