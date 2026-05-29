using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class RulesConfigVersionConfiguration : IEntityTypeConfiguration<RulesConfigVersion>
{
    public void Configure(EntityTypeBuilder<RulesConfigVersion> builder)
    {
        builder.Property(v => v.ConfigType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Content).IsRequired();
        builder.HasIndex(v => new { v.ConfigType, v.VersionNumber }).IsUnique();
    }
}
