using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Additive, idempotent seed of a handful of published pages under each of the four default
// root nodes (/wiki, /help, /support, /guidance). Skips any sample whose (root, segment) path
// already exists, so re-running only fills in what has been removed. Returns the number of
// pages actually created. Most samples are content-type (widget-tree body); a small number
// are wiki-type (raw HTML body) so the legacy wiki render path stays exercised by tests and
// manual browsing.
//
// Seeds PageNode / PageNodeVersion rows via IPageNodeService.CreatePageAsync ->
// SaveWorkingContentAsync -> PublishDraftAsync (NOT the retired WikiPage /
// WikiPageVersion entities, which the DropWikiPagePlumbing migration removed).
// "wiki" here is the PageType discriminator on PageNode, not the old WikiPage
// entity — see PageNodeService.CreatePageAsync + the wiki-typed Content saved as
// raw HTML for Wiki.cshtml consumption.
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
                    root.Id, sample.Segment, sample.Title, sample.PageType, userId);

                // Wiki pages persist the raw HTML directly; content pages persist a widget-tree
                // JSON. Wiki.cshtml pipes the stored string through IHtmlRenderingService, and
                // Content.cshtml deserialises via ContentPageJson — so the two shapes are mutually
                // exclusive and the seeder must pick the right one per PageType.
                var content   = sample.PageType == "wiki"
                    ? sample.HtmlBody
                    : BuildContentJson(sample.Heading, sample.HtmlBody);
                var plainText = sample.Heading + " " + StripTags(sample.HtmlBody);

                await pageNodes.SaveWorkingContentAsync(node.Id, content, plainText, userId);
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

    // Rudimentary tag stripper for the plain-text index — good enough for the tightly-scoped
    // seed content in this file, which is authored inline without ambiguous markup (no CDATA,
    // no <script>, no HTML comments). TODO: swap for the same HtmlToPlainText path used by
    // IHtmlRenderingService for editor-authored content — that already handles the edge cases
    // (CDATA sections, embedded scripts, entities) that this walker will happily misparse.
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

    private readonly record struct SamplePage(
        string Segment,
        string Title,
        string Heading,
        string HtmlBody,
        string PageType = "content");

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
                "<p>Requests flow from the web app into a Postgres queue, are processed by the rules engine worker, and matched decisions raise a Zendesk ticket via the outbox.</p>"),
            // Wiki-typed sample so Wiki.cshtml has a real page to exercise. Long-ish body with
            // multiple headings + paragraphs so scroll-dependent features (back-to-top, in-page
            // nav) have somewhere to breathe.
            new("wiki-sandbox",     "Wiki sandbox page",
                "Wiki sandbox page",
                "<p>This page is seeded specifically so the wiki render path has real content to exercise. It stores raw HTML in the version body, unlike the widget-tree JSON that content-type pages use.</p>" +
                "<h2>Why a dedicated wiki sample?</h2>" +
                "<p>Most CMS pages are content-typed and rendered through <code>Content.cshtml</code>. Wiki pages take a separate code path (<code>Wiki.cshtml</code>, <code>IHtmlRenderingService</code>), and without a live wiki page nothing exercises it — regressions there wouldn't surface until someone manually created one.</p>" +
                "<h2>Layout</h2>" +
                "<p>Wiki pages use a 1/3–2/3 grid with a sibling-nav in the left column and the article body in the right.</p>" +
                "<ul><li>Left column: sibling navigation</li><li>Right column: heading, subtitle, body HTML</li></ul>" +
                "<h2>Enough words for a good scroll</h2>" +
                "<p>The body deliberately runs long enough that a typical laptop viewport can scroll comfortably. If you want to add more test content just extend this HTML string.</p>" +
                "<p>Paragraph two — filler so the page has scroll depth for exercising the back-to-top link and any future long-form nav behaviour.</p>" +
                "<p>Paragraph three — additional filler so the section headings are far enough apart that the sibling nav in the sidebar has room to render the whole tree without collapsing.</p>" +
                "<p>Paragraph four — the final paragraph before the closing heading. When you click the back-to-top link from anywhere on the page, the browser should jump straight back to the H1 at the top of this article.</p>" +
                "<h2>End</h2>" +
                "<p>You have reached the end. Try the back-to-top link.</p>",
                PageType: "wiki")
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
                "<p>Performance results for sixth-form colleges, FE colleges and school sixth forms. Data is provisional until the checking window closes.</p>"),
            // Deliberately one short paragraph. GDS guidance says not to use the
            // back-to-top link on pages that fit the viewport, so the suppression
            // needs a page with no meaningful scroll depth to prove itself against.
            // Keep this body short — lengthening it silently weakens that check.
            new("short-page",       "Short page",
                "Short page",
                "<p>A deliberately short page. There is not enough content here to scroll, so no back-to-top link should appear.</p>")
        ])
    ];
}
