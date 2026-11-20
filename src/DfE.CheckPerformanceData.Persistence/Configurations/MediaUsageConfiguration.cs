using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class MediaUsageConfiguration : IEntityTypeConfiguration<MediaUsage>
{
    public void Configure(EntityTypeBuilder<MediaUsage> builder)
    {
        builder.ToTable("media_usages", t =>
            t.HasCheckConstraint("ck_media_usages_location", "location IN ('widget', 'richtext')"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.AssetId).HasColumnName("asset_id");
        builder.Property(x => x.AssetVersion).HasColumnName("asset_version");
        builder.Property(x => x.PageNodeId).HasColumnName("page_node_id");
        builder.Property(x => x.PageNodeVersionId).HasColumnName("page_node_version_id");
        builder.Property(x => x.Location).HasColumnName("location").IsRequired().HasMaxLength(20);
        builder.Property(x => x.WidgetId).HasColumnName("widget_id").HasMaxLength(100);

        // Restrict: the database refuses to delete an image version that a page version still uses.
        builder.HasOne<MediaAssetVersion>()
            .WithMany()
            .HasForeignKey(x => new { x.AssetId, x.AssetVersion })
            .HasConstraintName("fk_media_usages_media_asset_versions")
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade: usages are derived from page versions, and content import replaces a page's
        // versions by deleting them.
        builder.HasOne<PageNodeVersion>()
            .WithMany()
            .HasForeignKey(x => x.PageNodeVersionId)
            .HasConstraintName("fk_media_usages_page_node_versions")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PageNode>()
            .WithMany()
            .HasForeignKey(x => x.PageNodeId)
            .HasConstraintName("fk_media_usages_page_nodes")
            .OnDelete(DeleteBehavior.Cascade);

        // Where-used and the delete guard look up by image (and version).
        builder.HasIndex(x => new { x.AssetId, x.AssetVersion }).HasDatabaseName("ix_media_usages_asset_id_asset_version");
        // A page version's usages are replaced together when it is saved.
        builder.HasIndex(x => x.PageNodeVersionId).HasDatabaseName("ix_media_usages_page_node_version_id");
        builder.HasIndex(x => x.PageNodeId).HasDatabaseName("ix_media_usages_page_node_id");
    }
}
