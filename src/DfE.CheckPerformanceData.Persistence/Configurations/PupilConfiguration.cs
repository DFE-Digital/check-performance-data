using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class PupilConfiguration : IEntityTypeConfiguration<Pupil>
{
    public void Configure(EntityTypeBuilder<Pupil> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Surname).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Firstname).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Sex).HasMaxLength(1).IsRequired();
        builder.Property(p => p.DateOfBirth).HasMaxLength(10).IsRequired();
        builder.Property(p => p.Laestab).HasMaxLength(20).IsRequired();
        builder.Property(p => p.FirstLanguage).HasMaxLength(100).IsRequired();

        builder.HasIndex(p => new { p.CheckingWindowId, p.Laestab, p.Pincl, p.Surname, p.Firstname });
    }
}
