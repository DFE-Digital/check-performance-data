using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class AdminSectionAccessConfiguration : IEntityTypeConfiguration<AdminSectionAccess>
{
    public void Configure(EntityTypeBuilder<AdminSectionAccess> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.RoleName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.SectionKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(a => new { a.RoleName, a.SectionKey })
            .IsUnique();
    }
}
