using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Additive, idempotent seed of a handful of published content pages under each of the four
// default root nodes (/wiki, /help, /support, /guidance). Skips any sample whose (root, segment)
// path already exists, so re-running only fills in what has been removed. Returns the number of
// pages actually created.
public sealed class SamplePageNodeSeeder(IPageNodeService pageNodes)
{
    public async Task<int> SeedAsync(string? userId = "system")
    {
        var created = 0;
        foreach (var (rootSegment, samples) in SamplesByRoot)
        {
            var root = await pageNodes.GetNodeByPathAsync(rootSegment);
            if (root is null) continue;   // DefaultPageNodeSeeder should have created it.

            foreach (var sample in samples)
            {
                var samplePath = $"{rootSegment}/{sample.Segment}";
                if (await pageNodes.GetNodeByPathAsync(samplePath) is not null) continue;

                var node = await pageNodes.CreatePageAsync(
                    root.Id, sample.Segment, sample.Title, "content", userId);

                var contentJson = BuildContentJson(sample.Heading, sample.HtmlBody);
                var plainText   = sample.Heading + " " + StripTags(sample.HtmlBody);

                await pageNodes.SaveWorkingContentAsync(node.Id, contentJson, plainText, userId);
                await pageNodes.PublishDraftAsync(node.Id, userId);
                created++;
            }
        }

        return created;
    }

    // ── content builders ────────────────────────────────────────────────────

    // Single-column region containing a Heading widget and a RichText widget. The shape mirrors
    // what the editor would produce for a page authored by hand.
    private static string BuildContentJson(string heading, string htmlBody)
    {
        var tree = new List<ContentNode>
        {
            new RegionNode
            {
                Layout  = RegionLayout.Single,
                Columns =
                [
                    [
                        new WidgetNode { Type = "heading",  Props = ParseProps($"{{\"level\":2,\"text\":{JsonSerializer.Serialize(heading)}}}") },
                        new WidgetNode { Type = "richtext", Props = ParseProps($"{{\"html\":{JsonSerializer.Serialize(htmlBody)}}}") }
                    ]
                ]
            }
        };

        return ContentPageJson.Serialize(tree);
    }

    private static System.Text.Json.Nodes.JsonObject ParseProps(string json) =>
        System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

    // Rudimentary tag stripper for the plain-text index — good enough for seed content, which is
    // authored inline without ambiguous markup.
    private static string StripTags(string html)
    {
        var sb = new System.Text.StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    // ── sample catalogue ────────────────────────────────────────────────────

    private readonly record struct SamplePage(string Segment, string Title, string Heading, string HtmlBody);

    // Kept as a static array of (root segment, sample list) so the enumeration order is stable —
    // useful for tests that assert the created count and root-parenting.
    private static readonly (string RootSegment, SamplePage[] Samples)[] SamplesByRoot =
    [
        ("wiki",
        [
            new("dsi-roles",        "DSI roles and permissions",
                "DSI roles and permissions",
                "<p>The service uses two DfE Sign-In roles: <strong>cypmd_content_access_user</strong> for content editors and <strong>cypmd_admin</strong> for administrators. Admin implies editor.</p>"),
            new("rules-engine",     "Rules engine snapshot",
                "Rules engine snapshot",
                "<p>The rules engine picks jobs off a Postgres queue, evaluates them against a versioned rules snapshot pulled from Azure Blob Storage, and writes a decision back to the change request.</p>"),
            new("data-pipeline",    "Data pipeline overview",
                "Data pipeline overview",
                "<p>Requests flow from the web app into a Postgres queue, are processed by the rules engine worker, and matched decisions raise a Zendesk ticket via the outbox.</p>")
        ]),

        ("help",
        [
            new("getting-started",  "Getting started",
                "Getting started",
                "<p>Sign in with your DfE Sign-In account. Once signed in you can view your school's provisional performance data and submit amendments while the checking window is open.</p>"),
            new("submit-amendment", "Submit an amendment",
                "Submit an amendment",
                "<p>Open the record you want to change, click <strong>Amend</strong>, describe what should change and why, then submit. You will be notified by email when a reviewer picks it up.</p>"),
            new("faq",              "Frequently asked questions",
                "Frequently asked questions",
                "<p>Common questions about account access, the checking exercise and the amendment process. Try the search box on the top of this page if you cannot find your question.</p>")
        ]),

        ("support",
        [
            new("contact-helpline", "Contact the helpline",
                "Contact the helpline",
                "<p>Raise a <strong>Contact us</strong> request from the home page, or telephone the schools' helpline during opening hours. Include your DfE number so we can find your school's records.</p>"),
            new("common-issues",    "Common issues",
                "Common issues",
                "<p>Sign-in problems, missing school records, and unexpected performance figures are the three most common tickets. Check the guidance before raising a helpline request.</p>"),
            new("security-advice",  "Security advice",
                "Security advice",
                "<p>Follow DfE security guidance when handling pupil-level data. Do not share your sign-in credentials and only access the service from a trusted device.</p>")
        ]),

        ("guidance",
        [
            new("ks2-checking",     "KS2 checking window",
                "KS2 checking window",
                "<p>The KS2 checking exercise runs each year for primary schools. Review the provisional results for your school and submit amendments where the data looks wrong.</p>"),
            new("ks4-checking",     "KS4 checking window",
                "KS4 checking window",
                "<p>The main KS4 checking exercise covers secondary school results. A separate re-checking window opens in June for results affected by appeals and re-marks.</p>"),
            new("post-16",          "Post-16 performance data",
                "Post-16 performance data",
                "<p>Performance results for sixth-form colleges, FE colleges and school sixth forms. Data is provisional until the checking window closes.</p>")
        ])
    ];
}
