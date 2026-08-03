using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class SearchEventConfiguration : IEntityTypeConfiguration<SearchEvent>
{
    public void Configure(EntityTypeBuilder<SearchEvent> builder)
    {
        builder.ToTable("search_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(x => x.QueryRaw)
            .HasColumnName("query_raw");

        builder.Property(x => x.QueryNormalised)
            .HasColumnName("query_normalised");

        builder.Property(x => x.Scope)
            .HasColumnName("scope");

        builder.Property(x => x.ResultsPages)
            .HasColumnName("results_pages");

        builder.Property(x => x.ResultsBlocks)
            .HasColumnName("results_blocks");

        // Postgres computes these on insert. The sink writer sets only ResultsPages +
        // ResultsBlocks and Postgres derives the rest; the sink cannot drift out of
        // sync with the derived values because it never assigns them.
        builder.Property(x => x.ResultsTotal)
            .HasColumnName("results_total")
            .HasComputedColumnSql("results_pages + results_blocks", stored: true);

        builder.Property(x => x.ZeroResults)
            .HasColumnName("zero_results")
            .HasComputedColumnSql("(results_pages + results_blocks) = 0", stored: true);

        builder.Property(x => x.LatencyMs)
            .HasColumnName("latency_ms");

        // Marker for rows written by the sample-data seeder. Defaults to false so pre-
        // existing rows (from before the migration lands) remain classified as real
        // data. Delete-seeded on the admin surface filters on this column.
        builder.Property(x => x.IsSeeded)
            .HasColumnName("is_seeded")
            .HasDefaultValue(false);

        // Per-seed-run marker. Nullable — real user rows carry no job id at all. Indexed
        // because the Cancel/rollback query filters WHERE job_id = @id across a table
        // that can carry 100k+ rows; without the index the rollback would seq-scan.
        builder.Property(x => x.JobId)
            .HasColumnName("job_id");

        // Volume-over-time chart + retention purge both scan this column.
        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("ix_search_events_occurred_at");

        // Support lookup: WHERE session_id = @quoted_value.
        builder.HasIndex(x => x.SessionId)
            .HasDatabaseName("ix_search_events_session_id");

        // Top-queries drill-in groups by the normalised form.
        builder.HasIndex(x => x.QueryNormalised)
            .HasDatabaseName("ix_search_events_query_normalised");

        // Zero-result trend chart: without this partial index every dashboard render
        // seq-scans the whole table filtering on the computed zero_results flag.
        // Distinct property set (zero_results + occurred_at_utc) so EF does not
        // collapse this against the plain occurred_at index above; the filter
        // narrows the underlying pg index to zero-result rows only.
        builder.HasIndex(x => new { x.ZeroResults, x.OccurredAtUtc })
            .HasDatabaseName("ix_search_events_zero_results_occurred_at")
            .HasFilter("zero_results = true");

        // Volume-over-time chart groups by date_trunc('day'|'hour', occurred_at_utc) and
        // counts DISTINCT session_id per bucket. Without a composite key on the two columns
        // the planner falls back to a seq-scan + external merge sort on 100k+ rows; the
        // combined index lets the scan read rows in (time, session) order so the GROUP BY
        // can consume the stream without spilling to disk.
        builder.HasIndex(x => new { x.OccurredAtUtc, x.SessionId })
            .HasDatabaseName("ix_search_events_occurred_at_session_id");

        // Top-queries drill-in filters by time-window and groups by query_normalised.
        // The plain query_normalised index above helps the GROUP BY key but forces a
        // full scan through the index before filtering by time; a composite that leads
        // on occurred_at_utc lets the range predicate cut the scan first. Filtered to
        // NOT NULL so the query planner can drop the extra predicate.
        builder.HasIndex(x => new { x.OccurredAtUtc, x.QueryNormalised })
            .HasDatabaseName("ix_search_events_occurred_at_query_normalised")
            .HasFilter("query_normalised IS NOT NULL");

        // Per-seed-run rollback lookup. Filtered to non-NULL rows so real user activity
        // (job_id IS NULL) does not bloat the index. Only seeder-written rows land here,
        // and they are dropped shortly after by the Cancel/rollback action anyway.
        builder.HasIndex(x => x.JobId)
            .HasDatabaseName("ix_search_events_job_id")
            .HasFilter("job_id IS NOT NULL");
    }
}
