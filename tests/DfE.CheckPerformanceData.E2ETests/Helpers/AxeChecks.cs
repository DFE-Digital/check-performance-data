using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

/// <summary>
/// One axe run with the WCAG 2.1 AA rule set the admin accessibility sweep uses, so a page-specific
/// test can scan a state the sweep cannot reach (a form with errors, say) and report violations the
/// same way.
/// </summary>
public static class AxeChecks
{
    public static async Task ExpectNoViolationsAsync(IPage page, string pageName)
    {
        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions
            {
                Type = "tag",
                Values = new List<string> { "wcag2a", "wcag2aa", "wcag21a", "wcag21aa" },
            },
        });

        Assert.True(results.Violations.Length == 0, DescribeViolations(pageName, results.Violations));
    }

    // Rule id, impact, node count and the first two node selectors, so a failure is actionable
    // without re-running the scan by hand.
    private static string DescribeViolations(string pageName, AxeResultItem[] violations)
    {
        var sb = new StringBuilder();
        sb.Append(violations.Length).Append(" axe violation(s) on ").Append(pageName).Append(':').AppendLine();

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
}
