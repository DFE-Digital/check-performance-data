using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class DevZendeskTicketConfiguration : IEntityTypeConfiguration<DevZendeskTicket>
{
    public void Configure(EntityTypeBuilder<DevZendeskTicket> builder)
    {
        builder.ToTable("dev_zendesk_outbox");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ReferenceNumber)
            .HasColumnName("reference_number")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Subject)
            .HasColumnName("subject")
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.TicketId)
            .HasColumnName("ticket_id");

        builder.Property(x => x.RawJson)
            .HasColumnName("raw_json")
            .IsRequired();

        builder.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("ix_dev_zendesk_outbox_created_at");
    }
}
