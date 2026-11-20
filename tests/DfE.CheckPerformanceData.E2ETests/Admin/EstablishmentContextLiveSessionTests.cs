using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The establishment context guard refuses a POST that does not carry the EstablishmentContext
// field. The guard is live only for a session in the real cookie scheme, as DfE Sign-in gives.
// The dev cookie that the rest of the suite uses does not make such a session, so a form that
// leaves out the field passes every other test and fails only for real users.
//
// These tests sign in through the real cookie scheme. They check that the guard is live, that
// every POST form on the main admin pages carries the field, and that the actions which build
// their own forms still save.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class EstablishmentContextLiveSessionTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string ContextRefused = "The establishment context has changed";
    private readonly List<Guid> _createdPages = [];

    public override async Task DisposeAsync()
    {
        for (var i = _createdPages.Count - 1; i >= 0; i--)
        {
            await CmsSeedHelpers.TryDeletePageAsync(Fixture.SeedClient, _createdPages[i]);
        }
        await base.DisposeAsync();
    }

    // Replaces the dev cookie with a real cookie session, then proves the guard is live: the
    // page carries a context stamp only when the session has one.
    private async Task SignInWithALiveGuardAsync()
    {
        await Context.ClearCookiesAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/dev/impersonate/admin-session");
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin");
        Assert.Equal($"{Fixture.BaseUrl}/admin", Page.Url.TrimEnd('/'));
        var stamp = await Page.Locator("meta[name='establishment-context']").GetAttributeAsync("content");
        Assert.False(string.IsNullOrEmpty(stamp), "The real cookie session has no establishment context stamp.");
    }

    private async Task<(Guid Id, string Segment)> CreatePageAsync()
    {
        var segment = $"e2e-live-{Guid.NewGuid():N}"[..24];
        var id = await CmsSeedHelpers.CreatePageNodeAsync(
            Fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E live context page");
        _createdPages.Add(id);
        return (id, segment);
    }

    private async Task<string[]> PostFormsWithoutTheContextAsync() =>
        await Page.EvaluateAsync<string[]>(@"() => [...document.querySelectorAll('form')]
            .filter(form => (form.getAttribute('method') || '').toLowerCase() === 'post')
            .filter(form => !form.querySelector('input[name=""EstablishmentContext""]')?.value)
            .map(form => (form.id || form.closest('[id]')?.id || '(no id)') + ' -> ' + form.getAttribute('action'))");

    [Fact]
    public async Task EveryPostFormOnTheMainAdminPages_CarriesTheEstablishmentContext()
    {
        var (pageId, _) = await CreatePageAsync();
        await SignInWithALiveGuardAsync();

        string[] paths =
        [
            "/admin",
            "/admin/pages",
            $"/admin/pages/{pageId}/edit",
            "/admin/content-staging",
            "/admin/system-administration/logs",
            "/admin/home-banners",
            "/admin/content-blocks"
        ];
        var missing = new List<string>();
        foreach (var path in paths)
        {
            var response = await Page.GotoAsync($"{Fixture.BaseUrl}{path}");
            Assert.True(response?.Ok, $"{path} returned {response?.Status}");
            missing.AddRange((await PostFormsWithoutTheContextAsync()).Select(form => $"{path}: {form}"));
        }

        Assert.True(missing.Count == 0, "POST forms with no establishment context:\n" + string.Join("\n", missing));
    }

    [Fact]
    public async Task ChangingAPageUrlThroughTheConfirmDialog_Saves()
    {
        var (pageId, segment) = await CreatePageAsync();
        await SignInWithALiveGuardAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages/{pageId}/edit#properties");
        var details = Page.Locator("#cpb-rename-form");

        await details.Locator("input[name=segment]").FillAsync(segment + "-moved");
        await details.GetByRole(AriaRole.Button, new() { Name = "Save details" }).ClickAsync();
        var dialog = Page.Locator("#cpb-rename-confirm");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Yes, change URL" }).ClickAsync();

        await Assertions.Expect(Page.Locator("body")).Not.ToContainTextAsync(ContextRefused);
        await Assertions.Expect(details.Locator("input[name=segment]")).ToHaveValueAsync(segment + "-moved");
    }

    [Fact]
    public async Task CopyingAPageFromThePageTreeMenu_Saves()
    {
        var (pageId, _) = await CreatePageAsync();
        await SignInWithALiveGuardAsync();
        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/pages");

        // The page tree's menu builds a form in script and calls form.submit().
        await Page.Locator($"[data-page-node][data-page-id='{pageId}']").First.DispatchEventAsync("contextmenu");
        // The new page sits in a collapsed branch, so the menu can open off screen. Click its button
        // directly rather than by pointer.
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Copy page" }).Locator("button").DispatchEventAsync("click");
        await Page.WaitForURLAsync(url => Regex.IsMatch(url, @"/admin/pages/[0-9a-f-]{36}/edit")
            && !url.Contains(pageId.ToString(), StringComparison.OrdinalIgnoreCase));

        var copyId = Guid.Parse(Regex.Match(Page.Url, @"/admin/pages/([0-9a-f-]{36})/edit").Groups[1].Value);
        if (copyId != pageId) _createdPages.Add(copyId);
        await Assertions.Expect(Page.Locator("body")).Not.ToContainTextAsync(ContextRefused);
        Assert.NotEqual(pageId, copyId);
    }
}
