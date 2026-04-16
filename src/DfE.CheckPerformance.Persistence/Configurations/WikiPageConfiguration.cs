using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformance.Persistence.Configurations;

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
    }
}
