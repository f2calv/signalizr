using CasCap.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasCap.Data;

/// <summary>EF Core context for durable inbound messages and subscriber cursors.</summary>
public sealed class SignalizrDbContext(DbContextOptions<SignalizrDbContext> options) : DbContext(options)
{
    /// <summary>Persisted inbound messages ordered by monotonic identifier.</summary>
    public DbSet<InboundMessageEntity> InboundMessages => Set<InboundMessageEntity>();

    /// <summary>Durable binary content associated with inbound messages.</summary>
    public DbSet<InboundAttachmentEntity> InboundAttachments => Set<InboundAttachmentEntity>();

    /// <summary>Durable per-subscriber acknowledgement cursors.</summary>
    public DbSet<SubscriberCursorEntity> SubscriberCursors => Set<SubscriberCursorEntity>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboundMessageEntity>(entity =>
        {
            entity.ToTable("inbound_messages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(message => message.Channel).HasColumnName("channel");
            entity.Property(message => message.Sender).HasColumnName("sender");
            entity.Property(message => message.Message).HasColumnName("message");
            entity.Property(message => message.Timestamp).HasColumnName("timestamp");
            // A database default keeps the column additive: pods still on the previous release
            // insert without it while the new release rolls out.
            entity.Property(message => message.FromSelf).HasColumnName("from_self").HasDefaultValue(false);
            entity.Property(message => message.PollVoteTimestamp).HasColumnName("poll_vote_timestamp");
            entity.Property(message => message.PollVoteOptionIndexes).HasColumnName("poll_vote_option_indexes");
            entity.Property(message => message.PersistedAtUnixMilliseconds)
                .HasColumnName("persisted_at_unix_milliseconds");
            entity.HasIndex(message => message.PersistedAtUnixMilliseconds)
                .HasDatabaseName("ix_inbound_messages_persisted_at_unix_milliseconds");
        });

        modelBuilder.Entity<InboundAttachmentEntity>(entity =>
        {
            entity.ToTable("inbound_attachments");
            entity.HasKey(attachment => attachment.Id);
            entity.Property(attachment => attachment.Id).HasColumnName("id");
            entity.Property(attachment => attachment.InboundMessageId).HasColumnName("inbound_message_id");
            entity.Property(attachment => attachment.ContentType).HasColumnName("content_type");
            entity.Property(attachment => attachment.Filename).HasColumnName("filename");
            entity.Property(attachment => attachment.Content).HasColumnName("content");
            entity.HasIndex(attachment => attachment.InboundMessageId)
                .HasDatabaseName("ix_inbound_attachments_inbound_message_id");
            entity.HasOne(attachment => attachment.InboundMessage)
                .WithMany(message => message.Attachments)
                .HasForeignKey(attachment => attachment.InboundMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscriberCursorEntity>(entity =>
        {
            entity.ToTable("subscriber_cursors");
            entity.HasKey(cursor => cursor.SubscriberName);
            entity.Property(cursor => cursor.SubscriberName).HasColumnName("subscriber_name");
            entity.Property(cursor => cursor.LastAcknowledgedMessageId)
                .HasColumnName("last_acknowledged_message_id");
            entity.Property(cursor => cursor.UpdatedAtUtc).HasColumnName("updated_at_utc");
        });
    }
}
