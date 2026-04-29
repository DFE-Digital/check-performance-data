using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;


public sealed class CheckingWindowConfiguration : IEntityTypeConfiguration<CheckingWindow>
{
    public void Configure(EntityTypeBuilder<CheckingWindow> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.StartDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.EndDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.KeyStage)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);
    }
}

public sealed class CheckingWindow
{
    public Guid Id { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public KeyStages KeyStage { get; init; }
    public string Title { get; init; } = string.Empty;
}
