using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class CheckingWindow
{
    public Guid Id { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public KeyStages KeyStage { get; init; }
    public CheckingWindowType CheckingWindowType { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool Published { get; init; } = false;
    public string IngressFile { get; init; } = string.Empty;
    public string SchemaFile { get; init; } = string.Empty;
    public string IngressFileChecksum { get; init; } = string.Empty;
    public string SchemaFileChecksum { get; init; } = string.Empty;
    public WindowValidated? Validated { get; init; }
}

public sealed class WindowValidated
{
    public DateTime ValidatedAt { get; init; }
    public string IngressValidationChecksum { get; init; } = string.Empty;
    public string SchemaValidationChecksum { get; init; } = string.Empty;
}

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

        builder.Property(x => x.CheckingWindowType)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(x => x.IngressFile)
            .HasMaxLength(255);
        
        builder.Property(x => x.SchemaFile)
            .HasMaxLength(255);
        
        builder.Property(x => x.IngressFileChecksum)
            .HasMaxLength(256);
        
        builder.Property(x => x.SchemaFileChecksum)
            .HasMaxLength(256);
        
        builder.OwnsOne(x => x.Validated, validated =>
        {
            validated.Property(v => v.ValidatedAt);
            validated.Property(v => v.IngressValidationChecksum)
                .HasMaxLength(256);
            validated.Property(v => v.SchemaValidationChecksum)
                .HasMaxLength(256);
        });
    }
}