using DfE.CheckPerformanceData.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.TagHelpers;

public sealed class GovukConfirmModalTagHelperTests
{
    // --- ProcessAsync ---

    [Fact]
    public async Task ProcessAsync_EmitsDialogWithCorrectIdAndAriaAttributes()
    {
        var sut = MakeDestructiveSut(id: "confirm-delete-42");
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Null(output.TagName);
        Assert.Contains("<dialog id=\"confirm-delete-42\"", html);
        Assert.Contains("class=\"govuk-modal-dialogue__box\"", html);
        Assert.Contains("aria-labelledby=\"confirm-delete-42-title\"", html);
        Assert.Contains("aria-describedby=\"confirm-delete-42-body\"", html);
    }

    [Fact]
    public async Task ProcessAsync_DestructiveTrue_RendersCanonicalGovukWarningTextComponent()
    {
        var sut = MakeDestructiveSut();
        sut.WarningText = "This action cannot be undone.";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        // Canonical govuk-warning-text component markup (matches
        // https://design-system.service.gov.uk/components/warning-text/):
        Assert.Contains("<div class=\"govuk-warning-text\">", html);
        Assert.Contains(
            "<span class=\"govuk-warning-text__icon\" aria-hidden=\"true\">!</span>",
            html);
        Assert.Contains("<strong class=\"govuk-warning-text__text\">", html);
        Assert.Contains("<span class=\"govuk-visually-hidden\">Warning</span>", html);
        Assert.Contains("This action cannot be undone.", html);
        Assert.Contains("</strong>", html);
        Assert.Contains("govuk-button--warning", html);
    }

    [Fact]
    public async Task ProcessAsync_DestructiveTrue_WarningTextRenderedAfterHeadingAndBeforeBody()
    {
        var sut = MakeDestructiveSut();
        sut.Title = "Are you sure?";
        sut.WarningText = "WARN-MARKER";
        sut.Body = "BODY-MARKER";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        var headingIdx = html.IndexOf("Are you sure?", StringComparison.Ordinal);
        var warningIdx = html.IndexOf("WARN-MARKER", StringComparison.Ordinal);
        var bodyIdx = html.IndexOf("BODY-MARKER", StringComparison.Ordinal);

        Assert.True(headingIdx >= 0, "title must be rendered");
        Assert.True(warningIdx > headingIdx,
            "warning-text must appear AFTER the heading (heading first, then warning, then body)");
        Assert.True(bodyIdx > warningIdx,
            "body paragraph must appear AFTER warning-text");
    }

    [Fact]
    public async Task ProcessAsync_DestructiveFalse_OmitsWarningTextComponentAndWarningClass()
    {
        var sut = MakeDestructiveSut();
        sut.Destructive = false;
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("govuk-warning-text", html);
        Assert.DoesNotContain("govuk-button--warning", html);
    }

    [Fact]
    public async Task ProcessAsync_RendersChildContentInsideForm()
    {
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput(
            childHtml: "<input name=\"__RequestVerificationToken\" value=\"TOKEN\" />");

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        var formIndex = html.IndexOf("<form ", StringComparison.Ordinal);
        var tokenIndex = html.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        var titleIndex = html.IndexOf("govuk-heading-l", StringComparison.Ordinal);

        Assert.True(formIndex >= 0, "<form> must be emitted");
        Assert.True(tokenIndex > formIndex, "child content must appear after <form ...> opens");
        // Heading now renders inside __content but BEFORE the form, so child content
        // (which lives inside the form) appears after the heading in document order.
        Assert.True(tokenIndex > titleIndex, "child content must appear after the title heading");
    }

    [Fact]
    public async Task ProcessAsync_CancelControl_IsLinkWithDataConfirmCancelAndAutofocus()
    {
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();

        // Cancel must be a <a class="govuk-link" href="#" ...> per GDS button-page guidance,
        // NOT a <button class="govuk-button--secondary">.
        Assert.Contains("<a class=\"govuk-link\"", html);
        Assert.Contains("href=\"#\"", html);
        Assert.Contains("data-confirm-cancel", html);
        Assert.Contains(">Cancel</a>", html);
        Assert.Contains("autofocus", html);

        // Legacy markup must NOT appear:
        Assert.DoesNotContain("govuk-button--secondary", html);
    }

    [Fact]
    public async Task ProcessAsync_QuestionFormTitle_PassesThroughVerbatim()
    {
        var sut = MakeDestructiveSut();
        sut.Title = "Are you sure you want to delete 'Welcome'?";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        // Title rendered verbatim (with HTML-encoded apostrophes is OK; what matters is the
        // single-quote characters survive — encoded as &#39; — and the question mark is present).
        Assert.Contains("Are you sure you want to delete", html);
        Assert.Contains("?", html);
        Assert.Contains("Welcome", html);
        // Single-quote convention preserved (HTML encodes ' to &#39;).
        Assert.Contains("&#39;Welcome&#39;", html);
    }

    [Fact]
    public async Task ProcessAsync_TitleAndBody_AreHtmlEncoded()
    {
        var sut = MakeDestructiveSut();
        sut.Title = "Delete <evil>";
        sut.Body = "Body with \"quotes\" & ampersand";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains("Delete &lt;evil&gt;", html);
        Assert.Contains("&amp; ampersand", html);
        Assert.DoesNotContain("<evil>", html);
    }

    [Fact]
    public async Task ProcessAsync_WarningText_IsHtmlEncoded()
    {
        var sut = MakeDestructiveSut();
        sut.WarningText = "Hostile <script>alert(1)</script>";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains("Hostile &lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task ProcessAsync_FormUsesConfiguredActionAndMethod()
    {
        var sut = MakeDestructiveSut();
        sut.FormAction = "/help/delete/42";
        sut.FormMethod = "post";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains("<form action=\"/help/delete/42\" method=\"post\">", html);
    }

    // --- GDS modal-dialogue structural contract (Modal.md spec) ---

    [Fact]
    public async Task ProcessAsync_DoesNotRenderSiblingBackdropElement()
    {
        // .showModal() makes everything outside the dialog inert, so a sibling backdrop
        // element is unclickable. Dimming is provided by the dialog's ::backdrop pseudo
        // (CSS), and backdrop click-to-dismiss is wired in confirm-modal.js via the
        // dialog's own click event with e.target === dialog. The TagHelper must NOT emit
        // a sibling .govuk-modal-dialogue__backdrop element or an outer container.
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("govuk-modal-dialogue__backdrop", html);
        Assert.DoesNotContain("govuk-modal-dialogue__wrapper", html);
        Assert.DoesNotContain("class=\"govuk-modal-dialogue\"", html);
    }

    [Fact]
    public async Task ProcessAsync_DialogElement_UsesModalDialogueBoxClassAndAriaModal()
    {
        var sut = MakeDestructiveSut(id: "confirm-delete-42");
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains("<dialog ", html);
        Assert.Contains("class=\"govuk-modal-dialogue__box\"", html);
        Assert.Contains("aria-modal=\"true\"", html);
        Assert.Contains("aria-labelledby=\"confirm-delete-42-title\"", html);
        Assert.Contains("tabindex=\"-1\"", html);
    }

    [Fact]
    public async Task ProcessAsync_RendersHeaderBandWithCloseButton()
    {
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains("class=\"govuk-modal-dialogue__header\"", html);
        // X close button does NOT carry govuk-button — the global GDS .govuk-button rule
        // (green background) would otherwise win against the more specific dialogue-close
        // overrides because both are single-class selectors and govuk-frontend's stylesheet
        // loads after site.css. Sticking to the dialogue-close class alone keeps the black
        // header band consistent.
        Assert.Contains("class=\"govuk-modal-dialogue__close\"", html);
        Assert.DoesNotContain("class=\"govuk-button govuk-modal-dialogue__close\"", html);
        Assert.Contains("aria-label=\"close\"", html);
        Assert.Contains("data-element=\"govuk-modal-dialogue-close\"", html);

        // Header must precede the content section.
        var headerIdx = html.IndexOf("govuk-modal-dialogue__header", StringComparison.Ordinal);
        var contentIdx = html.IndexOf("govuk-modal-dialogue__content", StringComparison.Ordinal);
        Assert.True(contentIdx > headerIdx, "header must render before content");
    }

    [Fact]
    public async Task ProcessAsync_CloseButton_HasDistinctCloseAttributeNotConfirmCancel()
    {
        // The X close button uses data-modal-close so it doesn't compete with the
        // Cancel link for [data-confirm-cancel] focus targeting. Both close the
        // dialog; only the Cancel link is autofocused on open.
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        var closeButtonStart = html.IndexOf("govuk-modal-dialogue__close", StringComparison.Ordinal);
        var closeButtonEnd = html.IndexOf("</button>", closeButtonStart, StringComparison.Ordinal);
        var closeButtonMarkup = html[closeButtonStart..closeButtonEnd];

        Assert.Contains("data-modal-close", closeButtonMarkup);
        Assert.DoesNotContain("data-confirm-cancel", closeButtonMarkup);
        Assert.DoesNotContain("autofocus", closeButtonMarkup);
    }

    [Fact]
    public async Task ProcessAsync_Heading_UsesLargeSizeAndModalDialogueHeadingClass()
    {
        var sut = MakeDestructiveSut(id: "confirm-delete-42");
        sut.Title = "Are you sure?";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        // Heading must be govuk-heading-l (matches Modal.md + example screenshot) plus the
        // dialogue-specific BEM class. The previous govuk-heading-m must not appear.
        Assert.Contains(
            "<h2 class=\"govuk-modal-dialogue__heading govuk-heading-l\" id=\"confirm-delete-42-title\">",
            html);
        Assert.DoesNotContain("govuk-heading-m\"", html);
    }

    [Fact]
    public async Task ProcessAsync_BodyParagraph_UsesModalDialogueDescriptionClass()
    {
        var sut = MakeDestructiveSut(id: "confirm-delete-42");
        sut.Body = "BODY-MARKER";
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.Contains(
            "class=\"govuk-modal-dialogue__description govuk-body\"",
            html);
        Assert.Contains("id=\"confirm-delete-42-body\"", html);
        Assert.Contains("BODY-MARKER", html);
    }

    [Fact]
    public async Task ProcessAsync_ContentSectionWrapsHeadingFormAndButtons()
    {
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        var contentIdx = html.IndexOf("govuk-modal-dialogue__content", StringComparison.Ordinal);
        var headingIdx = html.IndexOf("govuk-modal-dialogue__heading", StringComparison.Ordinal);
        var formIdx = html.IndexOf("<form ", StringComparison.Ordinal);
        var buttonGroupIdx = html.IndexOf("govuk-button-group", StringComparison.Ordinal);

        Assert.True(contentIdx >= 0, "content section must render");
        Assert.True(headingIdx > contentIdx, "heading must render inside content");
        Assert.True(formIdx > contentIdx, "form must render inside content");
        Assert.True(buttonGroupIdx > formIdx, "button group must render inside the form");
    }

    [Fact]
    public void Site_Css_DoesNotForceDialogDisplay()
    {
        // Forcing `display: block` on dialog.govuk-modal-dialogue__box overrides the
        // UA default `dialog:not([open]) { display: none }`, making every modal markup
        // visible at page load before .showModal() runs. Guard against the regression.
        var css = File.ReadAllText(LocateSiteCss());

        var startIdx = css.IndexOf("dialog.govuk-modal-dialogue__box {", StringComparison.Ordinal);
        Assert.True(startIdx >= 0, "site.css must contain the dialog.govuk-modal-dialogue__box rule");

        var endIdx = css.IndexOf('}', startIdx);
        var ruleBody = css[startIdx..endIdx];

        // Allow display:none|inherit etc. explicitly; block the always-visible regression.
        Assert.DoesNotContain("display: block", ruleBody);
        Assert.DoesNotContain("display:block", ruleBody);
    }

    private static string LocateSiteCss()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir,
                "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "site.css");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Could not locate site.css from test base dir.");
    }

    [Fact]
    public async Task ProcessAsync_NoLegacyGovukConfirmModalClass()
    {
        // The old single-element dialog used class="govuk-confirm-modal" on the <dialog>.
        // The new structure uses govuk-modal-dialogue__box; the legacy class must not appear.
        var sut = MakeDestructiveSut();
        var (context, output) = MakeContextAndOutput();

        await sut.ProcessAsync(context, output);

        var html = output.Content.GetContent();
        Assert.DoesNotContain("class=\"govuk-confirm-modal\"", html);
    }

    // --- helpers ---

    private static GovukConfirmModalTagHelper MakeDestructiveSut(string id = "confirm-test") => new()
    {
        Id = id,
        Title = "Are you sure you want to delete 'Page'?",
        WarningText = "This action cannot be undone.",
        Body = "",
        ConfirmLabel = "Yes, delete",
        Destructive = true,
        FormAction = "/help/delete/1",
        FormMethod = "post"
    };

    private static (TagHelperContext, TagHelperOutput) MakeContextAndOutput(string childHtml = "")
    {
        var context = new TagHelperContext(
            tagName: "govuk-confirm-modal",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: Guid.NewGuid().ToString("N"));

        var output = new TagHelperOutput(
            tagName: "govuk-confirm-modal",
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
            {
                var content = new DefaultTagHelperContent();
                content.SetHtmlContent(childHtml);
                return Task.FromResult<TagHelperContent>(content);
            });

        return (context, output);
    }
}
