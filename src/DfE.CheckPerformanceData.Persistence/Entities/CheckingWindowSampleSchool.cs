using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// #937: a school in a window that sees sample data, not its real data. Ingress replaces the
/// school's rows in every exercise of the window with generated rows. A school is in the list of
/// a window at most once.
/// </summary>
public sealed class CheckingWindowSampleSchool
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; init; }

    /// <summary>Digits only (9334290), the form that every blob name uses.</summary>
    public string Laestab { get; init; } = string.Empty;

    /// <summary>Six digits. The generators need it when the school has no rows in the file.</summary>
    public string Urn { get; init; } = string.Empty;

    /// <summary>UTC instant of the add (Unspecified kind, as ChangeRequest.Timestamp is stored).</summary>
    public DateTime AddedAt { get; init; }

    /// <summary>The display name of the admin who added the school.</summary>
    public string AddedBy { get; init; } = string.Empty;
}

public sealed class CheckingWindowSampleSchoolConfiguration : IEntityTypeConfiguration<CheckingWindowSampleSchool>
{
    public void Configure(EntityTypeBuilder<CheckingWindowSampleSchool> builder)
    {
        builder.ToTable("CheckingWindowSampleSchools");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.Laestab).HasMaxLength(7).IsRequired();
        builder.Property(x => x.Urn).HasMaxLength(6).IsRequired();
        builder.Property(x => x.AddedAt).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(x => x.AddedBy).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.CheckingWindowId, x.Laestab }).IsUnique();

        // The list goes with its window.
        builder.HasOne<CheckingWindow>()
            .WithMany()
            .HasForeignKey(x => x.CheckingWindowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
