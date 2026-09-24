using CasCap.Data;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Services;

/// <summary>Reads durable inbound binary attachments retained with their parent messages.</summary>
public sealed class InboundAttachmentService(IDbContextFactory<SignalizrDbContext> dbContextFactory)
{
    /// <summary>Returns attachment bytes, or <see langword="null"/> when no attachment exists.</summary>
    public async Task<byte[]?> GetAsync(
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext.InboundAttachments
            .AsNoTracking()
            .Where(attachment => attachment.Id == attachmentId)
            .Select(attachment => attachment.Content)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}