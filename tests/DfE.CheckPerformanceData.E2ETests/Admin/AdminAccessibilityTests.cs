using System.Runtime.InteropServices;
using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Runs an axe-core scan over the admin surface ahead of an external accessibility audit.
// Five admin routes are checked against wcag2a/wcag2aa/wcag21a/wcag21aa: any violation
// fails the test with the rule id, its impact, how many nodes it hit, and the first couple
// of node selectors so a failure is actionable without re-running the scan by hand.
//
// Runs against a live container: Playwright drives a real Chromium browser through the
// deployment reachable at CPD_E2E_BASE_URL. Excluded from CI by the existing E2ETests
// filter — run it locally against a running deployment when accessibility work touches
// the admin area.
[Collection("E2E")]
public sealed class AdminAccessibilityTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1440, Height = 900 } };

    [SkippableTheory]
    [InlineData("/admin")]
    [InlineData("/admin/Search")]
    [InlineData("/admin/Search/Queries")]
    [InlineData("/admin/Search/ZeroResults")]
    [InlineData("/admin/messages")]
    public async Task AdminPage_HasNoWcagViolations(string path)
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Playwright browser test Linux-only");

        try
        {
            var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
            AttachCookieToContext(adminCookie);

            var response = await Page.GotoAsync($"{Fixture.BaseUrl}{path}");
            Assert.NotNull(response);
            Assert.Equal(200, response!.Status);

            await Page.Locator("#main-content").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            var results = await Page.RunAxe(new AxeRunOptions
            {
                RunOnly = new RunOnlyOptions
                {
                    Type = "tag",
                    Values = new List<string> { "wcag2a", "wcag2aa", "wcag21a", "wcag21aa" },
                },
            });

            Assert.True(results.Violations.Length == 0, DescribeViolations(path, results.Violations));
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        }
    }

    private static string DescribeViolations(string path, AxeResultItem[] violations)
    {
        var sb = new StringBuilder();
        sb.Append(violations.Length).Append(" axe violation(s) on ").Append(path).Append(':').AppendLine();

        foreach (var violation in violations)
        {
            sb.Append("  - ").Append(violation.Id)
              .Append(" (impact: ").Append(violation.Impact ?? "unknown")
              .Append(", nodes: ").Append(violation.Nodes.Length).Append(')').AppendLine();

            foreach (var node in violation.Nodes.Take(2))
            {
                sb.Append("      target: ").Append(JsonConvert.SerializeObject(node.Target)).AppendLine();
            }
        }

        return sb.ToString();
    }

    private void AttachCookieToContext(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader)) return;
        var equalsIndex = cookieHeader.IndexOf('=');
        if (equalsIndex <= 0) return;

        Context.AddCookiesAsync([new Cookie
        {
            Name = cookieHeader[..equalsIndex],
            Value = cookieHeader[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]).GetAwaiter().GetResult();
    }
}
