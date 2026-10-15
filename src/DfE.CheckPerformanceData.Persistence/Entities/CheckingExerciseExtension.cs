using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// AB#307773: extra time on one checking exercise for a named set of schools. A child of the
/// exercise, never a copy of it: the schools keep the same tab, data and current release, and
/// their requests stay stamped with the one exercise id. The exercise's own end date is what every
/// other school sees.
/// </summary>
public sealed class CheckingExerciseExtension
{
    public Guid Id { get; init; }
    public Guid CheckingExerciseId { get; init; }

    /// <summary>The named schools' end date, a UK wall-clock value like the exercise dates. Always
    /// later than the exercise's end when written; an early close moves it to the close.</summary>
    public DateTime EndDate { get; set; }

    /// <summary>UTC instant of the add (Unspecified kind, as ChangeRequest.Timestamp is stored).</summary>
    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// Set the first time this extension's requests are swept after it closes, by the admin's
    /// Send requests or by the automatic hand-over. While null (or while the extension is open)
    /// the exercise's own sweep leaves these schools alone.
    /// </summary>
    public DateTime? RequestsSentAt { get; set; }

    /// <summary>
    /// Bumped by every edit and early close. The edit page posts the version it showed and the
    /// write is a compare-and-set on it, so two admins changing only the school list cannot both
    /// pass (AB#307773 review, finding 3). Not a concurrency token: the writes are ExecuteUpdate.
    /// </summary>
    public int Version { get; set; }

    public List<CheckingExerciseExtensionSchool> Schools { get; init; } = [];
}

/// <summary>One school in an extension, by LAESTAB in digits-only form (9334290).</summary>
public sealed class CheckingExerciseExtensionSchool
{
    public Guid Id { get; init; }
    public Guid CheckingExerciseExtensionId { get; init; }

    /// <summary>Copied from the parent so the database can hold "one extension per school per
    /// exercise" as a unique index without a join. Never updated.</summary>
    public Guid CheckingExerciseId { get; init; }

    public string Laestab { get; init; } = string.Empty;
}

public sealed class CheckingExerciseExtensionConfiguration : IEntityTypeConfiguration<CheckingExerciseExtension>
{
    public void Configure(EntityTypeBuilder<CheckingExerciseExtension> builder)
    {
        builder.ToTable("CheckingExerciseExtensions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.EndDate).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(x => x.CreatedAt).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(x => x.CreatedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestsSentAt).HasColumnType("timestamp without time zone");
        builder.Property(x => x.Version).IsRequired().HasDefaultValue(0);
        builder.HasIndex(x => x.CheckingExerciseId);

        // The extensions go with their exercise, as its releases do.
        builder.HasOne<CheckingExercise>()
            .WithMany(e => e.Extensions)
            .HasForeignKey(x => x.CheckingExerciseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Schools)
            .WithOne()
            .HasForeignKey(s => s.CheckingExerciseExtensionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CheckingExerciseExtensionSchoolConfiguration : IEntityTypeConfiguration<CheckingExerciseExtensionSchool>
{
    public void Configure(EntityTypeBuilder<CheckingExerciseExtensionSchool> builder)
    {
        builder.ToTable("CheckingExerciseExtensionSchools");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Laestab).HasMaxLength(7).IsRequired();
        // A school is in at most one extension of an exercise. The service checks first so the
        // admin gets a sentence; this is the backstop.
        builder.HasIndex(x => new { x.CheckingExerciseId, x.Laestab }).IsUnique();
    }
}
