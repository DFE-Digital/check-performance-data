using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetterEntity>
{
    public void Configure(EntityTypeBuilder<DeadLetterEntity> builder)
    {
        builder.ToTable("queue_dead_letters");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.QueueName)
            .HasColumnName("queue_name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(x => x.PayloadHash)
            .HasColumnName("payload_hash")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.Attempts)
            .HasColumnName("attempts");

        builder.Property(x => x.EnqueuedAtUtc)
            .HasColumnName("enqueued_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.DeadLetteredAtUtc)
            .HasColumnName("dead_lettered_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.DeadLetteredAtUtc)
            .HasDatabaseName("ix_queue_dead_letters_dead_lettered_at");
    }
}
