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
    }
}
