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

    // #319: the validation stamp moved down to CheckingExercise. A window is no longer validated as
    // a whole — each exercise validates its own ingress + schema pair, on its own dates, and a
    // window is usable while another exercise is still unvalidated. Anything asking "is this window
    // validated" must now say which exercise it means, or fold the answer across all of them.

    /// <summary>
    /// The window's checking exercises, in sort order, and the only route to its ingress files —
    /// a dataset belongs to the exercise that consumes it. A configured window is meant to have at
    /// least one, and the window's own StartDate/EndDate equals the union of these rows — the admin
    /// wizard derives the outer pair rather than asking for it (#319), so the two cannot disagree.
    /// </summary>
    public List<CheckingExercise> CheckingExercises { get; init; } = [];
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
    }
}