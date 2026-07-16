using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class PageNodeVersionConfiguration : IEntityTypeConfiguration<PageNodeVersion>
{
    public void Configure(EntityTypeBuilder<PageNodeVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.MinorVersion).HasDefaultValue(0);
        builder.Property(v => v.Content).HasColumnType("text").IsRequired();
        builder.Property(v => v.BodyPlainText).IsRequired().HasDefaultValue("");
        builder.HasIndex(v => new { v.PageNodeId, v.VersionId }).IsUnique();
        builder.HasIndex(v => new { v.PageNodeId, v.IsCurrent });

        // Body-only search vector at weight D — lowest of the four weights so title / keywords
        // on the parent PageNode outrank a body-only hit. Combined with PageNode.SearchVector
        // in SearchPagesAsync via ts_rank sum.
        builder.Property(v => v.SearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                @"setweight(to_tsvector('english', coalesce(""BodyPlainText"", '')), 'D')",
                stored: true);

        builder.Property(v => v.SearchVector)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(v => v.SearchVector).HasMethod("gin");
    }
}
