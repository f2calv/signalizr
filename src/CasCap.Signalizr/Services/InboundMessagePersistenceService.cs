using CasCap.Data;
using CasCap.Data.Entities;
using CasCap.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Persists inbound messages and takes ownership of their wrapper attachments.</summary>
public sealed class InboundMessagePersistenceService(
    ILogger<InboundMessagePersistenceService> logger,
    IOptions<ReceiverConfig> receiverConfig,
    TimeProvider timeProvider,
    ISignalCliClient signalCliClient,
    IDbContextFactory<SignalizrDbContext> dbContextFactory)
{
    /// <summary>Downloads attachments, commits the durable message, then removes wrapper copies.</summary>
    public async Task PersistAsync(
        SignalReceivedMessage message,
        InboundDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        var sourceAttachments = ((IReceivedNotification)message).Attachments?
            .OfType<SignalReceivedAttachment>()
            .Where(attachment => !string.IsNullOrWhiteSpace(attachment.Id))
            .Select(attachment => (Attachment: attachment, Id: attachment.Id ?? string.Empty))
            .ToArray() ?? [];
        var entity = new InboundMessageEntity
        {
            Channel = delivery.Channel,
            Sender = delivery.Sender,
            Message = delivery.Message,
            Timestamp = delivery.Timestamp,
            FromSelf = delivery.FromSelf,
            PollVoteTimestamp = delivery.PollVote?.PollTimestamp,
            PollVoteOptionIndexes = delivery.PollVote is { } vote
                ? InboundPollVote.FormatOptionIndexes(vote.OptionIndexes)
                : null,
            PersistedAtUnixMilliseconds = timeProvider.GetUtcNow().ToUnixTimeMilliseconds()
        };

        foreach (var source in sourceAttachments)
            entity.Attachments.Add(await DownloadAttachmentAsync(source.Attachment, source.Id, cancellationToken).ConfigureAwait(false));

        await using (var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            dbContext.InboundMessages.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var deleteFailures = await DeleteWrapperAttachmentsAsync(sourceAttachments, cancellationToken).ConfigureAwait(false);
        if (deleteFailures > 0)
        {
            logger.LogWarning(
                "{ClassName} persisted inbound binaries but failed to delete {FailureCount} wrapper attachment(s)",
                nameof(InboundMessagePersistenceService), deleteFailures);
        }
    }

    private async Task<InboundAttachmentEntity> DownloadAttachmentAsync(
        SignalReceivedAttachment attachment,
        string attachmentId,
        CancellationToken cancellationToken)
    {
        ValidateAttachmentSize(attachment.Size);
        var content = await signalCliClient
            .GetAttachment(attachmentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The wrapper returned no attachment content.");
        ValidateAttachmentSize(content.Length);

        return new InboundAttachmentEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            ContentType = attachment.ContentType,
            Filename = attachment.Filename,
            Content = content
        };
    }

    private async Task<int> DeleteWrapperAttachmentsAsync(
        IReadOnlyList<(SignalReceivedAttachment Attachment, string Id)> sourceAttachments,
        CancellationToken cancellationToken)
    {
        var failures = 0;
        foreach (var source in sourceAttachments)
        {
            if (!await signalCliClient.DeleteAttachment(source.Id, cancellationToken).ConfigureAwait(false))
                failures++;
        }

        return failures;
    }

    private void ValidateAttachmentSize(long? size)
    {
        if (size > receiverConfig.Value.MaxAttachmentBytes)
        {
            throw new InvalidOperationException(
                $"An inbound attachment exceeds the configured {receiverConfig.Value.MaxAttachmentBytes} byte limit.");
        }
    }
}
