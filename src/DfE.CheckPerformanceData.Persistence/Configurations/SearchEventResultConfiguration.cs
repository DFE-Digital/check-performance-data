using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class SearchEventResultConfiguration : IEntityTypeConfiguration<SearchEventResult>
{
    public void Configure(EntityTypeBuilder<SearchEventResult> builder)
    {
        builder.ToTable("search_event_results");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.SearchEventId)
            .HasColumnName("search_event_id");

        builder.Property(x => x.Position)
            .HasColumnName("position");

        builder.Property(x => x.ResultKind)
            .HasColumnName("result_kind")
            .IsRequired();

        builder.Property(x => x.ResultKey)
            .HasColumnName("result_key")
            .IsRequired();

        builder.Property(x => x.Rank)
            .HasColumnName("rank");

        // Mirrors the parent SearchEvent.IsSeeded so the delete-seeded query does not
        // need to JOIN back to the parent to filter children.
        builder.Property(x => x.IsSeeded)
            .HasColumnName("is_seeded")
            .HasDefaultValue(false);

        builder.HasIndex(x => x.SearchEventId)
            .HasDatabaseName("ix_search_event_results_search_event_id");

        // Cascade delete: a session-scoped support purge drops the parent SearchEvent
        // row and Postgres removes the children automatically. Without CASCADE the
        // application code would need explicit child-cleanup logic and children could
        // orphan on a bug.
        builder.HasOne<SearchEvent>()
            .WithMany()
            .HasForeignKey(x => x.SearchEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
