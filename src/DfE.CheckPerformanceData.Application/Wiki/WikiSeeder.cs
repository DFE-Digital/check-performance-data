namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiSeeder(IWikiService wikiService, IWikiRepository repository)
{
    // The first slug created by BuildTree(). Used as the canonical idempotency
    // sentinel — if a root page with this slug already exists, the seeder no-ops.
    // Belt-and-braces with the GDS data-prevent-double-click on the admin tile;
    // closes the historic "wiki (2)/(3) duplicate" landmine where editors double-
    // clicked the seed button past the 1000ms client-side window.
    private const string CanonicalSeedRootSlug = "getting-started";

    private sealed record SeedPage(string Title, string Content, IReadOnlyList<SeedPage> Children)
    {
        public SeedPage(string title, string content) : this(title, content, []) { }
    }

    public async Task SeedAsync()
    {
        // Idempotency guard. SlugExistsAsync delegates to EF Core AnyAsync against
        // the WikiPages table (see WikiRepository.SlugExistsAsync). Returning early
        // when the canonical root is already present makes repeated invocations
        // safe regardless of the trigger (admin tile re-submit, scripted re-run).
        if (await repository.SlugExistsAsync(CanonicalSeedRootSlug, null))
        {
            return;
        }

        foreach (var root in BuildTree())
        {
            await CreateSubtreeAsync(root, parentId: null);
        }
    }

    private async Task CreateSubtreeAsync(SeedPage page, int? parentId)
    {
        var created = await CreateWithRetryAsync(page, parentId);
        foreach (var child in page.Children)
        {
            await CreateSubtreeAsync(child, created.Id);
        }
    }

    private async Task<WikiPageDto> CreateWithRetryAsync(SeedPage page, int? parentId)
    {
        var attempt = 1;
        var title = page.Title;
        while (true)
        {
            try
            {
                return await wikiService.CreatePageAsync(new CreateWikiPageDto
                {
                    Title = title,
                    Content = page.Content,
                    ParentId = parentId
                });
            }
            catch (DuplicateWikiPageException)
            {
                attempt++;
                title = $"{page.Title} ({attempt})";
            }
        }
    }

    private static IReadOnlyList<SeedPage> BuildTree() =>
    [
        new("Getting started",
            "## Getting started\n\nThis section explains how to access the Check Performance Data service and the prerequisites for headteachers and authorised users.\n\n<div class=\"govuk-warning-text\">\n  <span class=\"govuk-warning-text__icon\" aria-hidden=\"true\">!</span>\n  <strong class=\"govuk-warning-text__text\">\n    <span class=\"govuk-visually-hidden\">Warning</span>\n    Performance data shown in this service is provisional until the checking window closes. Do not share unpublished figures outside your school.\n  </strong>\n</div>",
            [
                new("Requesting an account",
                    "## Requesting an account\n\nHeadteachers and principals must request an account to access the checking exercise. Your details must match those held in **Get Information About Schools** (GIAS).\n\nUse the **Contact us** form if your details do not match."),
                new("Signing in",
                    "## Signing in\n\nSign in with your DfE Sign-in account once your access request has been approved. Sessions expire after 30 minutes of inactivity."),
                new("Roles and permissions",
                    "## Roles and permissions\n\nDifferent roles control what you can view and amend:\n\n- **Headteacher / Principal** — full access to checking and amendments\n- **School user** — read-only access to performance data\n- **Local authority** — multi-school overview")
            ]),

        new("Checking exercises",
            "## Checking exercises\n\nThe checking exercise lets schools verify their performance data before publication. Different exercises run for different key stages.",
            [
                new("Key Stage 2",
                    "## Key Stage 2\n\nKey Stage 2 (KS2) covers performance results for primary schools.",
                    [
                        new("KS2 checking",
                            "## KS2 checking\n\nThe live KS2 checking window. Review your school's results and submit amendments if anything is wrong."),
                        new("KS2 checked",
                            "## KS2 checked\n\nReview the results that have been finalised after the checking window closes.")
                    ]),
                new("Key Stage 4",
                    "## Key Stage 4\n\nKey Stage 4 (KS4) covers performance results for secondary schools, including a separate June re-checking window.",
                    [
                        new("KS4 June checking",
                            "## KS4 June checking\n\nThe re-marking window held in June for results affected by appeals and re-marks."),
                        new("KS4 checking",
                            "## KS4 checking\n\nThe main KS4 checking exercise. Review attainment and progress measures for your school."),
                        new("KS4 checked",
                            "## KS4 checked\n\nReview finalised KS4 results after the checking window closes.")
                    ]),
                new("16 to 18 performance data",
                    "## 16 to 18 performance data\n\nPerformance results for sixth-form colleges, FE colleges and school sixth forms.")
            ]),

        new("Submitting amendments",
            "## Submitting amendments\n\nIf you find an error in your data you can submit an amendment request during the open checking window.",
            [
                new("Reading the guidance",
                    "## Reading the guidance\n\nDownload and read the **Checking Instructions and Guidance** document for your exercise before submitting any amendments. Many requests can be answered by the guidance itself."),
                new("Errata for late re-marks",
                    "## Errata for late re-marks\n\nIf your data is wrong because of a late re-mark, follow the **errata** instructions on the Guidance / Documents page rather than the standard amendment process."),
                new("Tracking your request",
                    "## Tracking your request\n\nAmendment requests appear in your school's request log. You will be notified by email when a request is reviewed.")
            ]),

        new("Help and support",
            "## Help and support\n\nIf you cannot find what you need in the guidance documents, the support team can help.",
            [
                new("Frequently asked questions",
                    "## Frequently asked questions\n\nCommon questions about the checking exercise, account access, and submitting amendments."),
                new("Security advice",
                    "## Security advice\n\nFollow DfE security guidance when handling pupil-level data. Do not share your sign-in credentials and only access the service from a trusted device."),
                new("Contact the helpline",
                    "## Contact the helpline\n\nIf you cannot resolve your issue through the guidance, raise a **Contact us** request from the home page or telephone the schools' helpline.")
            ])
    ];
}
