using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class MediaAssetVersionConfiguration : IEntityTypeConfiguration<MediaAssetVersion>
{
    public void Configure(EntityTypeBuilder<MediaAssetVersion> builder)
    {
        builder.ToTable("media_asset_versions", t =>
            t.HasCheckConstraint("ck_media_asset_versions_version", "version >= 1"));

        // The natural key. Two replaces that race for the same number cannot both insert.
        builder.HasKey(x => new { x.AssetId, x.Version });

        builder.Property(x => x.AssetId).HasColumnName("asset_id");
        builder.Property(x => x.Version).HasColumnName("version").ValueGeneratedNever();

        builder.HasOne(x => x.Asset)
            .WithMany(a => a.Versions)
            .HasForeignKey(x => x.AssetId)
            .HasConstraintName("fk_media_asset_versions_media_assets_asset_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.BlobKey).HasColumnName("blob_key").IsRequired().HasMaxLength(400);
        builder.HasIndex(x => x.BlobKey).IsUnique().HasDatabaseName("ux_media_asset_versions_blob_key");

        builder.Property(x => x.ContentType).HasColumnName("content_type").IsRequired().HasMaxLength(100);
        builder.Property(x => x.Width).HasColumnName("width");
        builder.Property(x => x.Height).HasColumnName("height");
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");

        builder.Property(x => x.Sha256).HasColumnName("sha256").IsRequired().HasMaxLength(64).IsFixedLength();
        // Not unique: a duplicate upload is offered the existing image, not refused.
        builder.HasIndex(x => x.Sha256).HasDatabaseName("ix_media_asset_versions_sha256");

        builder.Property(x => x.IsAnimated).HasColumnName("is_animated");
        builder.Property(x => x.UploadedAtUtc).HasColumnName("uploaded_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.UploadedBy).HasColumnName("uploaded_by").HasMaxLength(200);
    }
}
