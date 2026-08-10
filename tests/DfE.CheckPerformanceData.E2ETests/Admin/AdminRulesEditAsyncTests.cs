using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Browser-driven coverage for the async branch editor (admin-rules.js): structural edits
// swap the condition tree in place without a full navigation, and a successful save shows
// the transient "Saved" toast while staying on the edit page. Linux-only [SkippableFact],
// matching the repo's Playwright-interaction convention (see AdminLandingGroupedTests).
[Collection("E2E")]
public sealed class AdminRulesEditAsyncTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string EditUrl = "/admin/rules/outcomes/Inclusion/branches/INC-REJ/edit";

    private async Task SignInAsAdminInBrowserAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (string.IsNullOrEmpty(adminCookie)) return;
        var equalsIndex = adminCookie.IndexOf('=');
        if (equalsIndex <= 0) return;
        await Context.AddCookiesAsync([new Cookie
        {
            Name = adminCookie[..equalsIndex],
            Value = adminCookie[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]);
    }

    // --- AddCondition_Updates_Editor_In_Place_Without_Navigation ---

    [SkippableFact]
    public async Task AddCondition_Updates_Editor_In_Place_Without_Navigation()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");

        try
        {
            await SignInAsAdminInBrowserAsync();
            await Page.GotoAsync($"{Fixture.BaseUrl}{EditUrl}");

            // INC-REJ loads as a single condition wrapped in an AllOf group.
            await Expect(Page.Locator(".rules-condition")).ToHaveCountAsync(1);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Add condition" }).First.ClickAsync();

            // The tree gains a second condition via the async fragment swap — no full reload.
            await Expect(Page.Locator(".rules-condition")).ToHaveCountAsync(2);
            Assert.EndsWith("/edit", Page.Url);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }

    // --- SaveBranch_Shows_Saved_Toast_And_Stays_On_Page ---

    [SkippableFact]
    public async Task SaveBranch_Shows_Saved_Toast_And_Stays_On_Page()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");

        try
        {
            await SignInAsAdminInBrowserAsync();
            await Page.GotoAsync($"{Fixture.BaseUrl}{EditUrl}");

            // INC-REJ is valid as-loaded, so saving it unchanged succeeds.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save branch" }).ClickAsync();

            var toast = Page.Locator(".rules-toast");
            await Expect(toast).ToBeVisibleAsync();
            await Expect(toast).ToHaveTextAsync("Saved");
            Assert.EndsWith("/edit", Page.Url); // async save stays on the editor, no redirect
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }

    // --- Collapse_Replaces_Group_With_Summary_And_Expand_Restores ---

    [SkippableFact]
    public async Task Collapse_Replaces_Group_With_Summary_And_Expand_Restores()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Playwright interaction test Linux-only");

        const string nestedEditUrl = "/admin/rules/outcomes/TerminalCriticalIllness/branches/TCI-KS4-REJ/edit";
        var combinators = Page.Locator("select[id*='Combinator']");

        try
        {
            await SignInAsAdminInBrowserAsync();
            await Page.GotoAsync($"{Fixture.BaseUrl}{nestedEditUrl}");

            var before = await combinators.CountAsync();
            Assert.True(before >= 2, "TCI-KS4-REJ should render multiple nested groups");

            // The toggle lives in each group's header (which renders before its children), so the
            // LAST Collapse button targets the innermost leaf-only group
            // (hasTerminalIllness false / hasSatExamsAsYear11 true) — no nested groups inside it.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Collapse" }).Last.ClickAsync();

            // That group is replaced by its read-only summary (async, no navigation). The line below
            // is unique to that group, so the locator is unambiguous.
            await Expect(Page.Locator(".rules-group--collapsed")).ToHaveCountAsync(1);
            await Expect(Page.GetByText("hasSatExamsAsYear11 is true")).ToBeVisibleAsync();
            await Expect(combinators).ToHaveCountAsync(before - 1); // one group's editor is gone
            Assert.EndsWith("/edit", Page.Url);

            // Expand restores the full editor.
            await Page.GetByRole(AriaRole.Button, new() { Name = "Expand" }).First.ClickAsync();
            await Expect(Page.Locator(".rules-group--collapsed")).ToHaveCountAsync(0);
            await Expect(combinators).ToHaveCountAsync(before);
        }
        finally { await AuthHelpers.ImpersonateAsEditorAsync(Fixture); }
    }
}
