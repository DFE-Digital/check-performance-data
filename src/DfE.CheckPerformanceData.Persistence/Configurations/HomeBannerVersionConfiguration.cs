using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class HomeBannerVersionConfiguration : IEntityTypeConfiguration<HomeBannerVersion>
{
    public void Configure(EntityTypeBuilder<HomeBannerVersion> builder)
    {
        builder
            .HasOne(v => v.HomeBanner)
            .WithMany(b => b.Versions)
            .HasForeignKey(v => v.HomeBannerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.HomeBannerId, v.VersionNumber }).IsUnique();

        builder.Property(v => v.Heading).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Body).IsRequired();
        builder.Property(v => v.ShowFrom).HasColumnType("timestamp without time zone");
        builder.Property(v => v.ShowUntil).HasColumnType("timestamp without time zone");
    }
}
