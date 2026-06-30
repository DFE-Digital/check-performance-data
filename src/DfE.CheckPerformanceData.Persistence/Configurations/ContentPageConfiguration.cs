using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        // Stable cross-environment identity; the DB default generates one unless import supplies it.
        builder.Property(p => p.ContentId)
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();

        builder.HasIndex(p => p.ContentId).IsUnique();

        // One live page per slug; soft-deleted rows are excluded so a slug can be reused after delete.
        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");

        builder.HasIndex(p => p.IsDeleted);
        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.Property(p => p.Slug).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Title).IsRequired();
        builder.Property(p => p.Layout).IsRequired().HasMaxLength(32);
        builder.Property(p => p.DraftContent).HasColumnType("jsonb").IsRequired();

        builder
            .HasMany(p => p.Versions)
            .WithOne(v => v.ContentPage)
            .HasForeignKey(v => v.ContentPageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
