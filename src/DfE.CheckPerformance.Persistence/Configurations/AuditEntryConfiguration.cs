using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .UseIdentityByDefaultColumn();

        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(10);

        builder.HasIndex(a => a.EntityType);
        builder.HasIndex(a => a.Timestamp);
    }
}
