using DfE.CheckPerformanceData.Application.CheckYourPupilData;
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

    /// <summary>
    /// What a school may *do* with this exercise. Null since #466: a display-only data share is an
    /// exercise the admin named and gave files to, with no journey behind it. Every kind lookup
    /// ignores a null row by construction — lifted equality means null matches no member.
    /// </summary>
    public CheckingExerciseType? ExerciseType { get; init; }

    /// <summary>
    /// True for a row on the exercise-id blob layout (#466), which is every row created since.
    /// False keeps the row on the old type-based paths, so blobs already written are still found
    /// and no blob migration is needed.
    /// </summary>
    public bool UsesExerciseStorage { get; init; } = true;

    /// <summary>The admin's name for this exercise, shown as the tab's heading.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// The tab label on Check Your Pupil Data. Null means the exercise draws no tab, which is what
    /// keeps every window configured before #466 rendering exactly as it did.
    /// </summary>
    public string? TabName { get; set; }

    /// <summary>Left-to-right order of the tabs.</summary>
    public int TabOrder { get; set; }

    /// <summary>False hides the exercise without deleting it, and without losing its files.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>How the tab shows the data. Set by the admin, never by the schemas.</summary>
    public ExerciseLayout Layout { get; set; } = ExerciseLayout.Table;

    /// <summary>The school may look at this data and download it, and do nothing else.</summary>
    public bool DisplayOnly { get; set; }

    /// <summary>When the tab appears and disappears. Null at either end means no bound.</summary>
    public DateTime? VisibleFrom { get; set; }
    public DateTime? VisibleUntil { get; set; }

    /// <summary>
    /// The exercise this one supersedes, when a later release replaces an earlier one. Restricted
    /// on delete: the row that was replaced must outlive the pointer to it.
    /// </summary>
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

    /// <summary>
    /// The release schools see. Null = no release yet, and the output (if any) is at the
    /// unversioned <c>exercises/{Id}/data/</c> prefix.
    /// </summary>
    /// <remarks>
    /// Deliberately not a foreign key. A key from here to the release table, plus the key from the
    /// release table back here, is a cycle that EF cannot order when an exercise is deleted. The
    /// only code that writes it (<c>CheckingExerciseDefinitionRepository</c>) sets it to a release
    /// of this exercise.
    /// </remarks>
    public Guid? CurrentReleaseId { get; set; }

    /// <summary>Every successful run of this exercise, each with its own output.</summary>
    public List<CheckingExerciseRelease> Releases { get; init; } = [];
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
        // A row with no kind is only meaningful as a display-only share on the new storage.
        // Anything else is a half-configured exercise that the journeys cannot route.
        builder.ToTable("CheckingExercises", table => table.HasCheckConstraint(
            "CK_CheckingExercises_TypeOrDisplayOnly",
            "\"ExerciseType\" IS NOT NULL OR (\"DisplayOnly\" AND \"UsesExerciseStorage\")"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ExerciseType)
            .IsRequired(false)
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

        builder.Property(x => x.Layout)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ExerciseLayout.Table);

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.TabName).HasMaxLength(100);
        builder.Property(x => x.VisibleFrom).HasColumnType("timestamp without time zone");
        builder.Property(x => x.VisibleUntil).HasColumnType("timestamp without time zone");
        builder.HasOne<CheckingExercise>().WithMany()
            .HasForeignKey(x => x.ReplacesCheckingExerciseId).OnDelete(DeleteBehavior.Restrict);

        // Only the legacy type-addressed rows need this. The new storage deliberately allows
        // several releases of one activity, and any number of typeless shares, in one window.
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
