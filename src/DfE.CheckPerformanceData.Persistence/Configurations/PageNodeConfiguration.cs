using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class PageNodeConfiguration : IEntityTypeConfiguration<PageNode>
{
    public void Configure(EntityTypeBuilder<PageNode> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever(); // app supplies GUIDs
        builder.Property(n => n.Segment).IsRequired().HasMaxLength(256);
        builder.Property(n => n.Path).IsRequired().HasMaxLength(2048);
        builder.Property(n => n.Title).IsRequired();
        builder.Property(n => n.Subtitle).HasMaxLength(1024);
        builder.Property(n => n.PageName).HasMaxLength(256);
        builder.Property(n => n.PageType).IsRequired().HasMaxLength(32);

        builder.HasIndex(n => n.Path).IsUnique().HasFilter("\"DeletedDate\" IS NULL");
        builder.HasIndex(n => n.ParentId);
        builder.HasQueryFilter(n => n.DeletedDate == null);

        // Default true at the DB level so backfilling existing rows on migration doesn't require
        // touching every row from application code, and inserts that don't set the value still
        // land with the safe "searchable" default.
        builder.Property(n => n.AppearInSearch).HasDefaultValue(true);

        // Full-text search vector, Postgres-computed on every insert/update, so the app never
        // has to keep it in sync by hand. Keywords weight A (highest), Title B, Subtitle C —
        // body text is indexed separately on PageNodeVersion.SearchVector and the two ranks
        // sum at query time in SearchPagesAsync.
        builder.Property(n => n.SearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                @"setweight(to_tsvector('english', coalesce(""Keywords"", '')), 'A') || "
                + @"setweight(to_tsvector('english', coalesce(""Title"", '')), 'B') || "
                + @"setweight(to_tsvector('english', coalesce(""Subtitle"", '')), 'C')",
                stored: true);

        builder.Property(n => n.SearchVector)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(n => n.SearchVector).HasMethod("gin");

        builder.HasOne<PageNode>().WithMany().HasForeignKey(n => n.ParentId)
            .HasConstraintName("FK_PageNode_PageNode_ParentId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(n => n.Versions).WithOne(v => v.PageNode)
            .HasForeignKey(v => v.PageNodeId).OnDelete(DeleteBehavior.Cascade);
    }
}
