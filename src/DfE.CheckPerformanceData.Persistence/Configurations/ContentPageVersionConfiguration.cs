using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ContentPageVersionConfiguration : IEntityTypeConfiguration<ContentPageVersion>
{
    public void Configure(EntityTypeBuilder<ContentPageVersion> builder)
    {
        builder.Property(v => v.Snapshot).HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.Title).IsRequired();

        // Version numbers are unique and sequential within a page.
        builder.HasIndex(v => new { v.ContentPageId, v.VersionNumber }).IsUnique();
    }
}
