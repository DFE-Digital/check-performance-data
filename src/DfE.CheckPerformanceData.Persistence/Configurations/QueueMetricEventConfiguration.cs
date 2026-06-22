using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class QueueMetricEventConfiguration : IEntityTypeConfiguration<QueueMetricEvent>
{
    public void Configure(EntityTypeBuilder<QueueMetricEvent> builder)
    {
        builder.ToTable("queue_metrics_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.QueueName)
            .HasColumnName("queue_name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Stage)
            .HasColumnName("stage")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ReferenceNumber)
            .HasColumnName("reference_number")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.MessageId)
            .HasColumnName("message_id");

        builder.Property(x => x.DecisionStatus)
            .HasColumnName("decision_status")
            .HasMaxLength(50);

        builder.Property(x => x.RulesVersion)
            .HasColumnName("rules_version")
            .HasMaxLength(100);

        builder.Property(x => x.LatencyMs)
            .HasColumnName("latency_ms");

        builder.Property(x => x.RecordedAtUtc)
            .HasColumnName("recorded_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.StartedAtUtc)
            .HasColumnName("started_at_utc")
            .HasColumnType("timestamp with time zone");

        // Time-window aggregation (throughput, dwell, decision-mix-over-time).
        builder.HasIndex(x => x.RecordedAtUtc)
            .HasDatabaseName("ix_queue_metrics_events_recorded_at");

        // Per-message journey timeline lookup.
        builder.HasIndex(x => new { x.ReferenceNumber, x.RecordedAtUtc })
            .HasDatabaseName("ix_queue_metrics_events_reference");

        // Per-queue throughput.
        builder.HasIndex(x => new { x.QueueName, x.RecordedAtUtc })
            .HasDatabaseName("ix_queue_metrics_events_queue_recorded");
    }
}
