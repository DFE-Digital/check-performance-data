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
    public bool UsesExerciseStorage { get; init; } = true;
    public string? Name { get; set; }
    public string? TabName { get; set; }
    public int TabOrder { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? VisibleFrom { get; set; }
    public DateTime? VisibleUntil { get; set; }
    public Guid? ReplacesCheckingExerciseId { get; set; }

    // Settable since #319: the admin wizard captures each exercise's dates, so an existing row has
    // to be able to take new ones. Before that nothing could change them once written.
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    /// <summary>Display order in the admin wizard and on any per-exercise list.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// The CSV + schema pairs this exercise ingests, in sort order. Any number, including none —
    /// an exercise whose files have not been loaded yet has an empty collection.
    /// </summary>
    public List<CheckingWindowDataset> Datasets { get; init; } = [];

    /// <summary>
    /// Set when this exercise's ingress + schema pair last validated cleanly. Null = not yet
    /// validated. Moved down from <see cref="CheckingWindow"/> (#319), which no longer carries it:
    /// each exercise has its own inputs and its own dates, so a single window-level flag could only
    /// ever describe one of them.
    /// </summary>
    public ExerciseValidated? Validated { get; set; }
}

/// <summary>
/// Renamed from <c>WindowValidated</c> (#319). Same shape, new owner.
/// </summary>
/// <remarks>
/// The two checksums are what make the stamp falsifiable rather than decorative: they are taken
/// over the exercise's datasets at the moment the run finished clean, so swapping an ingress file
/// afterwards leaves a stamp that visibly no longer describes the current files. The old
/// window-level stamp was written unconditionally on every create and update, so it recorded
/// nothing at all.
/// </remarks>
public sealed class ExerciseValidated
{
    public DateTime ValidatedAt { get; init; }
    public string IngressValidationChecksum { get; init; } = string.Empty;
    public string SchemaValidationChecksum { get; init; } = string.Empty;
}

public sealed class CheckingExerciseConfiguration : IEntityTypeConfiguration<CheckingExercise>
{
    public void Configure(EntityTypeBuilder<CheckingExercise> builder)
    {
        builder.ToTable("CheckingExercises");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.TabName).HasMaxLength(100);
        builder.Property(x => x.VisibleFrom).HasColumnType("timestamp without time zone");
        builder.Property(x => x.VisibleUntil).HasColumnType("timestamp without time zone");
        builder.HasOne<CheckingExercise>().WithMany()
            .HasForeignKey(x => x.ReplacesCheckingExerciseId).OnDelete(DeleteBehavior.Restrict);

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

        // Only historic Window/type paths need uniqueness. New exercise-ID storage allows
        // multiple releases and activities in the same scheduling Window.
        builder.HasIndex(x => new { x.CheckingWindowId, x.ExerciseType })
            .IsUnique().HasFilter("\"UsesExerciseStorage\" = false");

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
