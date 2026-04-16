using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformance.Persistence.Configurations;

internal sealed class ContentBlockVersionConfiguration : IEntityTypeConfiguration<ContentBlockVersion>
{
    public void Configure(EntityTypeBuilder<ContentBlockVersion> builder)
    {
        builder
            .HasOne(v => v.ContentBlock)
            .WithMany(b => b.Versions)
            .HasForeignKey(v => v.ContentBlockId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(v => new { v.ContentBlockId, v.VersionNumber })
            .IsUnique();
    }
}
