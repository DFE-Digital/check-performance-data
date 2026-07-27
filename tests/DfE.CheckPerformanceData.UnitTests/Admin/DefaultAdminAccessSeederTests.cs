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
}
