using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class QueueMessageConfiguration : IEntityTypeConfiguration<QueueMessageEntity>
{
    public void Configure(EntityTypeBuilder<QueueMessageEntity> builder)
    {
        builder.ToTable("queue_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.QueueName)
            .HasColumnName("queue_name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .IsRequired();

        builder.Property(x => x.Attempts)
            .HasColumnName("attempts");

        builder.Property(x => x.EnqueuedAtUtc)
            .HasColumnName("enqueued_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.VisibleAfterUtc)
            .HasColumnName("visible_after_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(x => new { x.QueueName, x.Status, x.VisibleAfterUtc })
            .HasDatabaseName("ix_queue_messages_claim");
    }
}
