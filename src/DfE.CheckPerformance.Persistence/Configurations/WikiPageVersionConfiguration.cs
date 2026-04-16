using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformance.Persistence.Configurations;

internal sealed class WikiPageVersionConfiguration : IEntityTypeConfiguration<WikiPageVersion>
{
    public void Configure(EntityTypeBuilder<WikiPageVersion> builder)
    {
        builder
            .HasOne(v => v.WikiPage)
            .WithMany(w => w.Versions)
            .HasForeignKey(v => v.WikiPageId)
            .HasConstraintName("FK_WikiPageVersion_WikiPage_WikiPageId")
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(v => new { v.WikiPageId, v.VersionNumber })
            .IsUnique();
    }
}
