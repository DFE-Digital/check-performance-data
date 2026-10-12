using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class HomeBannerConfiguration : IEntityTypeConfiguration<HomeBanner>
{
    public void Configure(EntityTypeBuilder<HomeBanner> builder)
    {
        builder.Property(b => b.ContentId)
            .HasDefaultValueSql("gen_random_uuid()")
            .ValueGeneratedOnAdd();
        builder.HasIndex(b => b.ContentId).IsUnique();

        builder.Property(b => b.Heading).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Body).IsRequired();

        // UK wall-clock values with no zone, the same column type as CheckingExercise.StartDate,
        // so Npgsql stores exactly what the editor typed and the rule compares like with like.
        builder.Property(b => b.ShowFrom).HasColumnType("timestamp without time zone");
        builder.Property(b => b.ShowUntil).HasColumnType("timestamp without time zone");

        builder.HasIndex(b => b.SortOrder);
    }
}
