using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// One successful ingress run of a checking exercise. The run wrote its per-school output under
/// its own prefix (<c>exercises/{exerciseId}/releases/{Id}/data/</c>), so no run ever overwrites the
/// output of an earlier one. <see cref="CheckingExercise.CurrentReleaseId"/> says which release
/// schools see.
/// </summary>
/// <remarks>
/// A row is written only when a run finishes clean. A run that fails leaves no row, and the
/// processor removes the files it wrote, so a half-written release can never become current.
/// </remarks>
public sealed class CheckingExerciseRelease
{
    public Guid Id { get; init; }
    public Guid CheckingExerciseId { get; init; }

    /// <summary>1, 2, 3 ... within the exercise. Unique per exercise.</summary>
    public int Number { get; init; }

    public DateTime PublishedAt { get; init; }
    public string PublishedBy { get; init; } = string.Empty;
    public int FilesWritten { get; init; }

    /// <summary>A copy of each dataset slot as the run read it.</summary>
    public List<CheckingExerciseReleaseFile> Files { get; init; } = [];
}

/// <summary>
/// One dataset slot as a release read it. A copy, not a foreign key to the slot: the slot can take
/// a new file after the run, and the release must still name the file that made its output.
/// </summary>
public sealed class CheckingExerciseReleaseFile
{
    public Guid Id { get; init; }
    public Guid CheckingExerciseReleaseId { get; init; }

    /// <summary>The slot the file came from. Names the dataset's output folder in the release.
    /// Not a foreign key: the release outlives a slot that is later removed.</summary>
    public Guid DatasetId { get; init; }
    public string DatasetName { get; init; } = string.Empty;
    public bool FeedsJourney { get; init; }
    public bool? Included { get; init; }
    public string? SourceFile { get; init; }
    public string IngressFile { get; init; } = string.Empty;
    public string IngressFileChecksum { get; init; } = string.Empty;
    public string SchemaFile { get; init; } = string.Empty;
    public string SchemaFileChecksum { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

public sealed class CheckingExerciseReleaseConfiguration : IEntityTypeConfiguration<CheckingExerciseRelease>
{
    public void Configure(EntityTypeBuilder<CheckingExerciseRelease> builder)
    {
        builder.ToTable("CheckingExerciseReleases");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PublishedAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.PublishedBy).HasMaxLength(320);

        builder.HasIndex(x => new { x.CheckingExerciseId, x.Number }).IsUnique();

        // The releases go with their exercise. An exercise is only deleted when the admin wizard
        // drops one that was never configured, and such a row has no releases to keep.
        builder.HasOne<CheckingExercise>()
            .WithMany(e => e.Releases)
            .HasForeignKey(x => x.CheckingExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Files)
            .WithOne()
            .HasForeignKey(f => f.CheckingExerciseReleaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CheckingExerciseReleaseFileConfiguration : IEntityTypeConfiguration<CheckingExerciseReleaseFile>
{
    public void Configure(EntityTypeBuilder<CheckingExerciseReleaseFile> builder)
    {
        builder.ToTable("CheckingExerciseReleaseFiles");
        builder.HasKey(x => x.Id);

        // Same limits as the CheckingWindowDatasets columns these are copied from.
        builder.Property(x => x.DatasetName).IsRequired().HasMaxLength(50);
        builder.Property(x => x.SourceFile).HasMaxLength(50);
        builder.Property(x => x.IngressFile).HasMaxLength(255);
        builder.Property(x => x.SchemaFile).HasMaxLength(255);
        builder.Property(x => x.IngressFileChecksum).HasMaxLength(256);
        builder.Property(x => x.SchemaFileChecksum).HasMaxLength(256);
    }
}
