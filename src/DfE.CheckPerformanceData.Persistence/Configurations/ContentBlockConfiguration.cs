using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder
            .HasIndex(b => b.Key)
            .IsUnique();

        // Stable cross-environment identity (see ContentBlock.ContentId).
        builder.Property(b => b.ContentId)
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        builder
            .HasIndex(b => b.ContentId)
            .IsUnique();

        // Default true at the DB level so backfilling existing rows on migration doesn't require
        // touching every row from application code, and inserts that don't set the value still
        // land with the safe "searchable" default.
        builder.Property(b => b.AppearInSearch).HasDefaultValue(true);

        // ValuePlainText is populated by ContentBlockService.SaveAsync (via IHtmlRenderingService
        // .StripTagsToPlainText) so the search vector matches on words rather than HTML markup.
        builder.Property(b => b.ValuePlainText).IsRequired().HasDefaultValue("");

        // Full-text search vector: Keywords weight A (highest), ValuePlainText weight B.
        builder.Property(b => b.SearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                // Weight 'A' — see SearchWeights.KeywordsWeight
                @"setweight(to_tsvector('english', coalesce(""Keywords"", '')), 'A') || "
                // Weight 'B' — see SearchWeights.ValueWeight
                + @"setweight(to_tsvector('english', coalesce(""ValuePlainText"", '')), 'B')",
                stored: true);

        builder.Property(b => b.SearchVector)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(b => b.SearchVector).HasMethod("gin");
    }
}
