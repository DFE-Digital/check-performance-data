using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class AppLogConfiguration : IEntityTypeConfiguration<AppLog>
{
    public void Configure(EntityTypeBuilder<AppLog> builder)
    {
        builder.HasKey(a => a.Id);

        // bigserial — the log table is write-heavy so bigint headroom matters.
        builder.Property(a => a.Id)
            .UseIdentityByDefaultColumn()
            .HasColumnType("bigint");

        builder.Property(a => a.Level).IsRequired().HasMaxLength(32);
        builder.Property(a => a.Category).IsRequired().HasMaxLength(256);
        builder.Property(a => a.Message).IsRequired();
        builder.Property(a => a.Exception);
        builder.Property(a => a.RequestPath).HasMaxLength(1024);
        builder.Property(a => a.UserId).HasMaxLength(256);
        builder.Property(a => a.CorrelationId).HasMaxLength(128);

        // StateJson goes into a jsonb column so we can query by property later.
        builder.Property(a => a.StateJson).HasColumnType("jsonb");

        // "Most recent first" is the dominant query — index Timestamp DESC.
        builder.HasIndex(a => a.Timestamp).IsDescending();

        // Level + Category are the two filter dropdowns; both selective enough
        // to warrant their own indexes.
        builder.HasIndex(a => a.Level);
        builder.HasIndex(a => a.Category);
    }
}
