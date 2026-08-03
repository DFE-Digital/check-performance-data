using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;

namespace DfE.CheckPerformanceData.UnitTests.Admin;

// Locks in the two nav-key constants and the corresponding AllSections entries so the
// admin gate + section-access grid stay in sync. Downstream plans hang RequireAdminSection
// attributes off AdminNavKeys.SearchAnalytics / MessagesInbox; without the AllSections
// entry the seeder never grants admins access to either surface, so the [RequireAdminSection]
// gate returns 404 on a fresh DB and the phase would ship broken.
public sealed class DefaultAdminAccessSeederTests
{
    [Fact]
    public void AdminNavKeys_SearchAnalytics_HasKebabCaseValue()
    {
        Assert.Equal("search-analytics", AdminNavKeys.SearchAnalytics);
    }

    [Fact]
    public void AdminNavKeys_MessagesInbox_HasKebabCaseValue()
    {
        Assert.Equal("messages-inbox", AdminNavKeys.MessagesInbox);
    }

    [Fact]
    public void AllSections_ContainsSearchAnalytics()
    {
        Assert.Contains("search-analytics", DefaultAdminAccessSeeder.AllSections);
    }

    [Fact]
    public void AllSections_ContainsMessagesInbox()
    {
        Assert.Contains("messages-inbox", DefaultAdminAccessSeeder.AllSections);
    }

    // The two lists MUST stay in sync — the seeder's comment above the list literally
    // says so, and the [RequireAdminSection(AdminNavKeys.X)] gate only lets a user
    // through if AdminSectionAccess has a matching row for their role. Guards against
    // silently drifting one const without the other.
    [Fact]
    public void AllSections_MatchesAdminNavKeysConstants()
    {
        Assert.Contains(AdminNavKeys.SearchAnalytics, DefaultAdminAccessSeeder.AllSections);
        Assert.Contains(AdminNavKeys.MessagesInbox, DefaultAdminAccessSeeder.AllSections);
    }

    // The Test-data sub-group's landing / seed controllers are gated by the group key
    // itself so the SUM of "may I hit this surface at all?" collapses to a single admin
    // section grant. Absence would 404 the seeder page for a fresh-DB admin.
    [Fact]
    public void AdminNavKeys_TestDataGroup_HasKebabCaseValue()
    {
        Assert.Equal("test-data-group", AdminNavKeys.TestDataGroup);
    }

    [Fact]
    public void AdminNavKeys_SeedSampleSearchData_HasKebabCaseValue()
    {
        Assert.Equal("seed-sample-search-data", AdminNavKeys.SeedSampleSearchData);
    }

    [Fact]
    public void AllSections_ContainsTestDataGroup()
    {
        Assert.Contains("test-data-group", DefaultAdminAccessSeeder.AllSections);
    }

    // The tile itself needs its own grant so FilterByAccess renders it in the sidebar
    // for admins (canAccessSection is checked per-tile-Key on entries that have a URL).
    // Without this the admin would see the group container heading but nothing under it.
    [Fact]
    public void AllSections_ContainsSeedSampleSearchData()
    {
        Assert.Contains("seed-sample-search-data", DefaultAdminAccessSeeder.AllSections);
    }
}
