using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
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
    }
}
