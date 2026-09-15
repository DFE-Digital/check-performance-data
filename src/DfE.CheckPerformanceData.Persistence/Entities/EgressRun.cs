using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// One data-egress run (AB#294553): a pull of Zendesk decisions for one window and one or more
/// output types, carried through preprocessing to transfer. Persisted at pull time so "save" is
/// nothing more than leaving the page and a resume never re-pulls.
/// </summary>
public sealed class EgressRun
{
    public Guid Id { get; set; }
    public Guid WindowId { get; set; }
    public EgressRunStatus Status { get; set; }
    public Guid StartedById { get; set; }
    public string StartedByName { get; set; } = string.Empty;
    public string? StartedByEmail { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? PreprocessedAtUtc { get; set; }
    /// <summary>The London calendar date stamped into every file name, fixed when preprocessing completes so summary and transfer agree.</summary>
    public DateOnly? ExportDate { get; set; }
    public DateTime? TransferredAtUtc { get; set; }
    public string? TransferredByName { get; set; }
    /// <summary>JSON array of EgressRecordFailure when preprocessing failed; null otherwise.</summary>
    public string? FailureJson { get; set; }
    /// <summary>Why the last transfer failed, for the summary page; null otherwise.</summary>
    public string? TransferFailureReason { get; set; }
    public List<EgressRunOutput> Outputs { get; set; } = [];
}

/// <summary>
/// One output type within a run. IsActive is the concurrency lock: true from pull until the run
/// fails or is abandoned, and it STAYS true after a successful transfer so the same window and
/// type can never be sent twice. A partial unique index on (WindowId, OutputType) WHERE IsActive
/// is what refuses the second run; WindowId is duplicated here for that index.
/// </summary>
public sealed class EgressRunOutput
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid WindowId { get; set; }
    public EgressOutputType OutputType { get; set; }
    public bool IsActive { get; set; }
    /// <summary>JSON array of EgressSourceRecord — the "data pulled" the Results screen shows and a resume reuses.</summary>
    public string RawRecordsJson { get; set; } = "[]";
    public int SourceRecordCount { get; set; }
    public int? OutputRecordCount { get; set; }
    public string? FileName { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class EgressRunConfiguration : IEntityTypeConfiguration<EgressRun>
{
    public void Configure(EntityTypeBuilder<EgressRun> builder)
    {
        builder.ToTable("egress_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.StartedByName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.StartedByEmail).HasMaxLength(256);
        builder.Property(x => x.TransferredByName).HasMaxLength(200);
        builder.Property(x => x.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.PreprocessedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.TransferredAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(x => x.TransferFailureReason).HasMaxLength(1000);
        builder.HasOne<CheckingWindow>().WithMany().HasForeignKey(x => x.WindowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Outputs).WithOne().HasForeignKey(o => o.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.WindowId);
        builder.HasIndex(x => x.StartedAtUtc);
    }
}

public sealed class EgressRunOutputConfiguration : IEntityTypeConfiguration<EgressRunOutput>
{
    public void Configure(EntityTypeBuilder<EgressRunOutput> builder)
    {
        builder.ToTable("egress_run_outputs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OutputType).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RawRecordsJson).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(100);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        // The concurrency rule (AB#294553): one ACTIVE output per window and type. Partial, so a
        // failed or abandoned run (IsActive = false) never blocks a new one, while a transferred
        // run (still active) blocks forever.
        builder.HasIndex(x => new { x.WindowId, x.OutputType })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE")
            .HasDatabaseName("ix_egress_run_outputs_active_window_output");
        builder.HasIndex(x => x.RunId);
    }
}
