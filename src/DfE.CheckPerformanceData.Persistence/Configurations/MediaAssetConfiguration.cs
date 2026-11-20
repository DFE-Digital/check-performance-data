using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets", t =>
            t.HasCheckConstraint("ck_media_assets_current_version", "current_version >= 1"));

        builder.HasKey(x => x.Id);
        // App-supplied: the first version's blob key is built from the id before the row exists.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.FileName).HasColumnName("file_name").IsRequired().HasMaxLength(255);
        builder.Property(x => x.Slug).HasColumnName("slug").IsRequired().HasMaxLength(200);
        builder.Property(x => x.DefaultAltText).HasColumnName("default_alt_text").HasMaxLength(1000);
        builder.Property(x => x.Caption).HasColumnName("caption").HasMaxLength(2000);
        builder.Property(x => x.Credit).HasColumnName("credit").HasMaxLength(500);

        // A Postgres text[]: an array keeps a tag that holds a space or a comma intact, which a
        // delimited string would not. Never null, so a filter needs no null check.
        builder.Property(x => x.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]")
            .IsRequired()
            .HasDefaultValueSql("'{}'::text[]");

        builder.Property(x => x.CurrentVersion).HasColumnName("current_version");

        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);

        // The library lists the most recently changed images first.
        builder.HasIndex(x => x.UpdatedAtUtc).HasDatabaseName("ix_media_assets_updated_at_utc");
    }
}
