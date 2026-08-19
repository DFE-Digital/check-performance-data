using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// One ingress CSV + JSON schema pair for a checking exercise. The pupil-data exercise of a Post16
/// window has two rows (included + non-included) because LDS supplies 16-19 pupils as two files;
/// every other type has one. Both files are ingested in a single run and merged into one blob per
/// school. An exercise may hold any number of these rows, including none.
/// </summary>
public sealed class CheckingWindowDataset
{
    public Guid Id { get; init; }

    /// <summary>The exercise that consumes this file pair.</summary>
    public Guid CheckingExerciseId { get; set; }

    /// <summary>
    /// Legacy. Kept for one release so the release can be rolled back, and dropped by a follow-up
    /// migration once every environment has moved. No reader may use it.
    /// </summary>
    public Guid CheckingWindowId { get; set; }

    public string Name { get; init; } = string.Empty;
    public string IngressFile { get; set; } = string.Empty;
    public string IngressFileChecksum { get; set; } = string.Empty;
    public string SchemaFile { get; set; } = string.Empty;
    public string SchemaFileChecksum { get; set; } = string.Empty;

    /// <summary>Null = inclusion comes from the record's own P_INCL (KS4).</summary>
    public bool? Included { get; init; }

    public int SortOrder { get; init; }
}

public sealed class CheckingWindowDatasetConfiguration : IEntityTypeConfiguration<CheckingWindowDataset>
{
    public void Configure(EntityTypeBuilder<CheckingWindowDataset> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.IngressFile).HasMaxLength(255);
        builder.Property(x => x.SchemaFile).HasMaxLength(255);
        builder.Property(x => x.IngressFileChecksum).HasMaxLength(256);
        builder.Property(x => x.SchemaFileChecksum).HasMaxLength(256);

        // Names are unique within an exercise, not within a window: two exercises of the same
        // window may each hold a dataset called "pupils".
        builder.HasIndex(x => new { x.CheckingExerciseId, x.Name }).IsUnique();

        builder.HasOne<CheckingExercise>()
            .WithMany(e => e.Datasets)
            .HasForeignKey(x => x.CheckingExerciseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
