using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class SearchMessageConfiguration : IEntityTypeConfiguration<SearchMessage>
{
    public void Configure(EntityTypeBuilder<SearchMessage> builder)
    {
        builder.ToTable("search_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(x => x.SubmittedAtUtc)
            .HasColumnName("submitted_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.WhatLookingFor)
            .HasColumnName("what_looking_for")
            .IsRequired();

        builder.Property(x => x.WhatGot)
            .HasColumnName("what_got");

        builder.Property(x => x.Email)
            .HasColumnName("email");

        builder.Property(x => x.IsRead)
            .HasColumnName("is_read")
            .HasDefaultValue(false);

        builder.Property(x => x.ReadByAdminSub)
            .HasColumnName("read_by_admin_sub");

        builder.Property(x => x.ReadAtUtc)
            .HasColumnName("read_at_utc")
            .HasColumnType("timestamp with time zone");

        // Marker for rows written by the sample-data seeder. Defaults to false so real
        // user-submitted messages survive the delete-seeded admin action.
        builder.Property(x => x.IsSeeded)
            .HasColumnName("is_seeded")
            .HasDefaultValue(false);

        // Support lookup keyed by the session id the user quotes.
        builder.HasIndex(x => x.SessionId)
            .HasDatabaseName("ix_search_messages_session_id");

        // Inbox listing default-sorts by submitted-at DESC.
        builder.HasIndex(x => x.SubmittedAtUtc)
            .HasDatabaseName("ix_search_messages_submitted_at");

        // Unread-count badge on _AdminLayout.cshtml renders on every admin page, so the
        // COUNT(*) WHERE is_read = false lookup must not seq-scan.
        builder.HasIndex(x => x.IsRead)
            .HasDatabaseName("ix_search_messages_is_read");
    }
}
