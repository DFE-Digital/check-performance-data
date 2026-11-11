using Dfe.Analytics.EFCore;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

/// <summary>
/// Opts tables in to the Airbyte → BigQuery sync. Tables are opt-in, so anything not listed here is not synced.
/// Columns holding personal data are synced as hidden (policy-tagged in BigQuery).
/// Deliberately not synced: audit/log/queue tables, settings, share tokens, Zendesk payloads,
/// CMS content, search messages (free text and email) and egress output payloads.
/// </summary>
internal static class AnalyticsSyncConfiguration
{
    public static void ConfigureAnalyticsSync(this ModelBuilder modelBuilder)
    {
        var adminSectionAccess = modelBuilder.Entity<ChangeRequest>();
        adminSectionAccess.IncludeInAnalyticsSync(includeAllColumns: true, hidden: false);
        adminSectionAccess.Property(e => e.PupilFirstname).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.PupilId).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.PupilSurname).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.PupilUpn).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.SubmittedByName).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.SubmittedByEmail).ConfigureAnalyticsSync(hidden: true);
        adminSectionAccess.Property(e => e.WithdrawnByEmail).ConfigureAnalyticsSync(hidden: true);
    }
}
