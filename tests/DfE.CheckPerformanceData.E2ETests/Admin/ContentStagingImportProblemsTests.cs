using System.Net;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// An import goes ahead when one page in the bundle is broken: that page, and the page beneath
// it, are listed as not imported, and the rest lands. A segment whose only fault is capital
// letters is lowercased, with a warning. A whole-site export once failed to import at all over a
// single page whose segment had a capital letter in it.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ContentStagingImportProblemsTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    [Fact]
    public async Task A_broken_page_is_left_out_and_a_segment_with_capitals_is_lowercased()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var root = Guid.NewGuid();
        var broken = Guid.NewGuid();
        var bundle = new
        {
            schema = "cpd-content-v2",
            schemaVersion = 2,
            pageNodes = new object[]
            {
                new { id = root, segment = $"import-problems-{run}", title = $"Import problems {run}", pageType = "folder", versions = Array.Empty<object>() },
                new { id = Guid.NewGuid(), parentId = root, segment = $"Mixed-Case-{run}", title = $"Mixed case {run}", pageType = "folder", versions = Array.Empty<object>() },
                new { id = broken, parentId = root, segment = "bad segment", title = $"Broken {run}", pageType = "folder", versions = Array.Empty<object>() },
                new { id = Guid.NewGuid(), parentId = broken, segment = "beneath", title = $"Beneath {run}", pageType = "folder", versions = Array.Empty<object>() },
            },
            contentBlocks = Array.Empty<object>(),
        };
        var json = JsonSerializer.Serialize(bundle).Replace("\"schema\":", "\"$schema\":");

        await Page.GotoAsync($"{Fixture.BaseUrl}/admin/content-staging");
        // The enhanced GOV.UK file upload (#632) moves the id onto its button, so reach the native
        // input by name, as every other SetInputFilesAsync in the suite does.
        await Page.Locator("input[name='bundle']").SetInputFilesAsync(new FilePayload
        {
            Name = "bundle.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(json)
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Preview import" }).ClickAsync();

        // The preview lists the broken page with its reason and the page beneath it, and warns
        // about the lowercasing, instead of refusing the bundle.
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Review import");
        var problems = Page.Locator(".govuk-error-summary");
        await Expect(problems).ToContainTextAsync($"Broken {run}");
        await Expect(problems).ToContainTextAsync("invalid Segment 'bad segment'");
        await Expect(problems).ToContainTextAsync($"Beneath {run}");
        await Expect(problems).ToContainTextAsync($"Its parent page ‘Broken {run}’ cannot be imported.");
        await Expect(Page.Locator(".govuk-notification-banner"))
            .ToContainTextAsync($"has URL segment 'Mixed-Case-{run}', so it is imported as 'mixed-case-{run}'.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm import" }).ClickAsync();
        await Page.WaitForURLAsync($"{Fixture.BaseUrl}/admin/content-staging");

        // The summary says the same, and the good pages are there.
        var body = Page.Locator("main.admin-main");
        await Expect(body).ToContainTextAsync($"Did not import ‘Broken {run}’");
        await Expect(body).ToContainTextAsync($"Could not import ‘Beneath {run}’ — its parent ‘Broken {run}’ was not imported.");
        await Expect(body).ToContainTextAsync($"so it is imported as 'mixed-case-{run}'.");

        using var tree = new HttpRequestMessage(HttpMethod.Get, $"{Fixture.BaseUrl}/admin/pages?q={run}");
        var response = await Fixture.SeedClient.SendAsync(tree);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var treeHtml = await response.Content.ReadAsStringAsync();
        Assert.Contains($"mixed-case-{run}", treeHtml);
        Assert.DoesNotContain($"Mixed-Case-{run}", treeHtml);
        Assert.DoesNotContain($"Broken {run}", treeHtml);
    }
}
