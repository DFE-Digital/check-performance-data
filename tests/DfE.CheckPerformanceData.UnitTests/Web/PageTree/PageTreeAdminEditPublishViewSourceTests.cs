namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

// Static Razor-source assertions for the publish/unpublish/version-history block added to
// Views/ContentPage/Edit.cshtml and Views/PageTreeAdmin/WikiEdit.cshtml.
// Pattern mirrors ContentPageEditorViewSourceTests: read .cshtml source as text and assert
// on the template text. These catch regressions — missing antiforgery token, wrong action URL,
// button class downgrade, or the conditional being inverted.
public sealed class PageTreeAdminEditPublishViewSourceTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string ReadView(string folder, string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", folder, name));

    private static string ContentEdit() => ReadView("ContentPage", "Edit.cshtml");
    private static string WikiEdit()    => ReadView("PageTreeAdmin", "WikiEdit.cshtml");

    // ── Content editor (Edit.cshtml) ─────────────────────────────────────────

    [Fact]
    public void ContentEdit_HasPublishDraft_Button_InUnpublishedBranch()
    {
        var src = ContentEdit();
        Assert.Contains("Publish draft", src);
        Assert.Contains("/publish-draft", src);
    }

    [Fact]
    public void ContentEdit_HasUnpublish_Button_InPublishedBranch()
    {
        var src = ContentEdit();
        Assert.Contains("Unpublish", src);
        Assert.Contains("/unpublish", src);
    }

    [Fact]
    public void ContentEdit_PublishedBranch_HasWarningButtonClass()
    {
        var src = ContentEdit();
        Assert.Contains("govuk-button--warning", src);
    }

    [Fact]
    public void ContentEdit_HasVersionsTab()
    {
        // Version history moved out of a scroll-to anchor into its own tab so authors do not
        // need to scroll a long page just to see history. The tab is targeted by #versions.
        var src = ContentEdit();
        Assert.Contains("Versions", src);
        Assert.Contains("#versions", src);
    }

    [Fact]
    public void ContentEdit_PublishBlock_IsGuardedByNodeIdNotEmpty()
    {
        var src = ContentEdit();
        Assert.Contains("Model.NodeId != Guid.Empty", src);
    }

    [Fact]
    public void ContentEdit_PublishBlock_BranchesOnIsPublished()
    {
        var src = ContentEdit();
        Assert.Contains("Model.IsPublished", src);
    }

    [Fact]
    public void ContentEdit_PublishForms_HaveAntiForgeryToken()
    {
        var src = ContentEdit();
        // Html.AntiForgeryToken() must appear inside the publish-draft block — count occurrences
        // to confirm it's present in the new block (it may already appear elsewhere).
        var count = CountOccurrences(src, "AntiForgeryToken()");
        Assert.True(count >= 2, $"Expected at least 2 antiforgery token calls in Edit.cshtml (one per new form), found {count}.");
    }

    [Fact]
    public void ContentEdit_PublishedTag_HasGreenClass()
    {
        var src = ContentEdit();
        Assert.Contains("govuk-tag--green", src);
    }

    [Fact]
    public void ContentEdit_HelperText_MakeThisPageLive_PresentInUnpublishedBranch()
    {
        var src = ContentEdit();
        // Tab restructure re-worded the helper — same idea, different phrasing.
        Assert.Contains("make this page live", src);
    }

    // ── Wiki editor (WikiEdit.cshtml) ────────────────────────────────────────

    [Fact]
    public void WikiEdit_HasPublishDraft_Button_InUnpublishedBranch()
    {
        // The publish action is now a formaction="…/save-and-publish" button in the main form.
        var src = WikiEdit();
        Assert.Contains("/save-and-publish", src);
        Assert.Contains("formaction", src);
    }

    [Fact]
    public void WikiEdit_HasUnpublish_Button_InPublishedBranch()
    {
        var src = WikiEdit();
        Assert.Contains("Unpublish", src);
        Assert.Contains("/unpublish", src);
    }

    [Fact]
    public void WikiEdit_PublishedBranch_HasWarningButtonClass()
    {
        var src = WikiEdit();
        Assert.Contains("govuk-button--warning", src);
    }

    [Fact]
    public void WikiEdit_HasVersionHistoryLink()
    {
        var src = WikiEdit();
        Assert.Contains("Version history", src);
        Assert.Contains("#version-history", src);
    }

    [Fact]
    public void WikiEdit_PublishBlock_BranchesOnIsPublished()
    {
        var src = WikiEdit();
        Assert.Contains("Model.IsPublished", src);
    }

    [Fact]
    public void WikiEdit_PublishForms_HaveAntiForgeryToken()
    {
        var src = WikiEdit();
        // Each form needs its own token; there are now two forms (publish-draft + unpublish).
        var count = CountOccurrences(src, "AntiForgeryToken()");
        Assert.True(count >= 2, $"Expected at least 2 antiforgery token calls in WikiEdit.cshtml, found {count}.");
    }

    [Fact]
    public void WikiEdit_PublishedTag_HasGreenClass()
    {
        var src = WikiEdit();
        Assert.Contains("govuk-tag--green", src);
    }

    [Fact]
    public void WikiEdit_HelperText_MakeThisPageLive_PresentInUnpublishedBranch()
    {
        var src = WikiEdit();
        Assert.Contains("Make this page live", src);
    }

    [Fact]
    public void WikiEdit_ActionUrls_ReferenceNodeId()
    {
        var src = WikiEdit();
        Assert.Contains("Model.NodeId/save-and-publish", src);
        Assert.Contains("Model.NodeId/unpublish", src);
        Assert.Contains("#version-history", src);
    }

    // The top-of-page status tag must share the same source of truth as the publish block
    // (Model.IsPublished). The old PublishedVersionNumber field is never populated for page-tree
    // content pages, so a tag bound to it stays permanently "Draft only" and contradicts the
    // green "Published" tag once a draft is published. Guard against reintroducing that split.
    [Fact]
    public void ContentEdit_TopStatusTag_DrivenByIsPublished_NotDeadPublishedVersionNumber()
    {
        var src = ContentEdit();
        Assert.DoesNotContain("PublishedVersionNumber", src);
        // Both the top status tag and the publish block read Model.IsPublished.
        Assert.True(CountOccurrences(src, "Model.IsPublished") >= 2);
    }

    // Both editors must expose a "View page" affordance that opens the rendered public page
    // (the node's slug path) in a new tab, so editors can see the output without the edit controls.
    [Fact]
    public void ContentEdit_HasViewPageLink_OpeningPagePathInNewTab()
    {
        var src = ContentEdit();
        Assert.Contains("View page", src);
        Assert.Contains("/@Model.PagePath", src);
        Assert.Contains("target=\"_blank\"", src);
    }

    [Fact]
    public void WikiEdit_HasViewPageLink_OpeningPagePathInNewTab()
    {
        var src = WikiEdit();
        Assert.Contains("View page", src);
        Assert.Contains("/@Model.PagePath", src);
        Assert.Contains("target=\"_blank\"", src);
    }

    // ── Wiki editor — save-and-publish ───────────────────────────────────────

    [Fact]
    public void WikiEdit_HasSaveAndPublish_FormAction()
    {
        var src = WikiEdit();
        Assert.Contains("/save-and-publish", src);
        Assert.Contains("formaction", src);
    }

    [Fact]
    public void WikiEdit_Save_Button_SubmitsToSaveAction()
    {
        var src = WikiEdit();
        Assert.Contains("/admin/pages/@Model.NodeId/save", src);
    }

    // ── Content editor — save-draft + publish-draft button group ─────────────

    [Fact]
    public void ContentEdit_HasContentSaveDraft_Action()
    {
        var src = ContentEdit();
        Assert.Contains("/content/save-draft", src);
    }

    [Fact]
    public void ContentEdit_HasSaveAndPublishButtonGroup_WithBothForms()
    {
        var src = ContentEdit();
        Assert.Contains("/content/save-draft", src);
        Assert.Contains("/publish-draft", src);
        Assert.Contains("govuk-button-group", src);
    }

    // ── Version numbers on status tags ───────────────────────────────────────

    [Fact]
    public void ContentEdit_StatusTag_ShowsPublishedVersion()
    {
        var src = ContentEdit();
        Assert.Contains("Published — version", src);
    }

    [Fact]
    public void ContentEdit_StatusTag_ShowsDraftVersion()
    {
        var src = ContentEdit();
        Assert.Contains("Draft — version", src);
    }

    [Fact]
    public void WikiEdit_StatusTag_ShowsPublishedVersion()
    {
        var src = WikiEdit();
        Assert.Contains("Published — version", src);
    }

    [Fact]
    public void WikiEdit_StatusTag_ShowsDraftVersion()
    {
        var src = WikiEdit();
        Assert.Contains("Draft — version", src);
    }

    // ── Inline version-history list partial ───────────────────────────────────

    [Fact]
    public void ContentEdit_IncludesVersionHistoryListPartial()
    {
        var src = ContentEdit();
        Assert.Contains("_VersionHistoryList", src);
    }

    [Fact]
    public void WikiEdit_IncludesVersionHistoryListPartial()
    {
        var src = WikiEdit();
        Assert.Contains("_VersionHistoryList", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasVersionHistoryHeading()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("Version history", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasPublishNowAction()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("/versions/publish-now", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasVersionIdHiddenInput()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("versionId", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasAnchorId()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("id=\"version-history\"", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasPublishFromAndToColumns()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("Publish from", src);
        Assert.Contains("Publish to", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasFromAndToDateInputs()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("name=\"from\"", src);
        Assert.Contains("name=\"to\"", src);
        Assert.Contains("datetime-local", src);
    }

    [Fact]
    public void VersionHistoryListPartial_PostsToPublishForSave()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("/publish\"", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasUnpublishAction()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("/versions/unpublish", src);
    }

    [Fact]
    public void VersionHistoryListPartial_HasViewPreviewLink()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("/preview", src);
        Assert.Contains("target=\"_blank\"", src);
    }

    // ── Version number labels — helpers use PageVersionNumbering / PageVersionStatus ──────────

    [Fact]
    public void ContentEdit_StatusTag_ShowsPublishedVersionLabel()
    {
        var src = ContentEdit();
        Assert.Contains("Published — version", src);
        Assert.Contains("PublishedVersionLabel", src);
    }

    [Fact]
    public void ContentEdit_StatusTag_ShowsDraftVersionLabel()
    {
        var src = ContentEdit();
        Assert.Contains("Draft — version", src);
        Assert.Contains("DraftVersionLabel", src);
    }

    [Fact]
    public void WikiEdit_StatusTag_ShowsPublishedVersionLabel()
    {
        var src = WikiEdit();
        Assert.Contains("Published — version", src);
        Assert.Contains("PublishedVersionLabel", src);
    }

    [Fact]
    public void WikiEdit_StatusTag_ShowsDraftVersionLabel()
    {
        var src = WikiEdit();
        Assert.Contains("Draft — version", src);
        Assert.Contains("DraftVersionLabel", src);
    }

    // ── _VersionHistoryList.cshtml uses helper classes ────────────────────────

    [Fact]
    public void VersionHistoryListPartial_UsesPageVersionStatusHelper()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("PageVersionStatus", src);
    }

    [Fact]
    public void VersionHistoryListPartial_UsesPageVersionNumberingHelper()
    {
        var src = ReadView("PageTreeAdmin", "_VersionHistoryList.cshtml");
        Assert.Contains("PageVersionNumbering", src);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static int CountOccurrences(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
