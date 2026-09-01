using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ContentStagingSessionConfiguration : IEntityTypeConfiguration<ContentStagingSession>
{
    public void Configure(EntityTypeBuilder<ContentStagingSession> builder)
    {
        builder.ToTable("content_staging_sessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        // Unbounded on purpose: the ceiling on bundle size belongs at the upload, where it can be
        // enforced against the file length before anything is parsed. A column length here would
        // be a second, silent limit that fails after the work is done.
        builder.Property(x => x.BundleJson)
            .HasColumnName("bundle_json")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone");

        // Every read filters on expiry, and the sweep deletes by it.
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_content_staging_sessions_expires_at_utc");
    }
}
