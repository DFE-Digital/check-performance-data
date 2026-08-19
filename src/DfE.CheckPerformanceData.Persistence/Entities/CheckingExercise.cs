using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// One activity inside a checking window, on its own date range. A window type with a single
/// exercise has one row on the window's own dates; a window type with several has one row each,
/// and the window's outer StartDate/EndDate is their union.
/// </summary>
/// <remarks>
/// Each exercise has its own inputs, on its own dates, validated against its own schemas — so the
/// ingress CSV + schema pairs hang off this entity rather than off the window.
/// </remarks>
public sealed class CheckingExercise
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; set; }
    public CheckingExerciseType ExerciseType { get; init; }
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }

    /// <summary>Display order in the admin wizard and on any per-exercise list.</summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// The CSV + schema pairs this exercise ingests, in sort order. Any number, including none —
    /// an exercise whose files have not been loaded yet has an empty collection.
    /// </summary>
    public List<CheckingWindowDataset> Datasets { get; init; } = [];
}

public sealed class CheckingExerciseConfiguration : IEntityTypeConfiguration<CheckingExercise>
{
    public void Configure(EntityTypeBuilder<CheckingExercise> builder)
    {
        builder.ToTable("CheckingExercises");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ExerciseType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.StartDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.EndDate)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.HasOne<CheckingWindow>()
            .WithMany(w => w.CheckingExercises)
            .HasForeignKey(x => x.CheckingWindowId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per exercise type per window: the lookup #315 does. This caps repeats of a type,
        // never how many types a window may hold.
        builder.HasIndex(x => new { x.CheckingWindowId, x.ExerciseType }).IsUnique();
    }
}
