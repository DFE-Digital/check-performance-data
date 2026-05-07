using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class WikiPageConfiguration : IEntityTypeConfiguration<WikiPage>
{
    public void Configure(EntityTypeBuilder<WikiPage> builder)
    {
        builder
            .HasOne(w => w.Parent)
            .WithMany(w => w.Children)
            .HasForeignKey(w => w.ParentId)
            .HasConstraintName("FK_WikiPage_WikiPage_ParentId")
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(w => new { w.ParentId, w.Slug })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder
            .HasIndex(w => w.IsDeleted);

        builder
            .HasQueryFilter(w => !w.IsDeleted);

        builder.Property(w => w.BodyPlainText)
            .IsRequired()
            .HasDefaultValue("");

        builder.Property(w => w.SearchVector)
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                @"setweight(to_tsvector('english', coalesce(""Title"", '')), 'A') || "
                + @"setweight(to_tsvector('english', coalesce(""BodyPlainText"", '')), 'B')",
                stored: true);

        builder.Property(w => w.SearchVector)
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasIndex(w => w.SearchVector).HasMethod("gin");
    }
}
