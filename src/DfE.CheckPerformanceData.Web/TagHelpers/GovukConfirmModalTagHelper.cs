using System.Net;
using System.Text;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DfE.CheckPerformanceData.Web.TagHelpers;

[HtmlTargetElement("govuk-confirm-modal", TagStructure = TagStructure.NormalOrSelfClosing)]
public sealed class GovukConfirmModalTagHelper : TagHelper
{
    [HtmlAttributeName("id")]
    public string Id { get; set; } = string.Empty;

    [HtmlAttributeName("title")]
    public string Title { get; set; } = string.Empty;

    [HtmlAttributeName("warning-text")]
    public string WarningText { get; set; } = string.Empty;

    [HtmlAttributeName("body")]
    public string Body { get; set; } = string.Empty;

    [HtmlAttributeName("confirm-label")]
    public string ConfirmLabel { get; set; } = string.Empty;

    [HtmlAttributeName("destructive")]
    public bool Destructive { get; set; }

    [HtmlAttributeName("form-action")]
    public string FormAction { get; set; } = string.Empty;

    [HtmlAttributeName("form-method")]
    public string FormMethod { get; set; } = "post";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var childHtml = (await output.GetChildContentAsync()).GetContent();

        output.TagName = null;

        var encodedId = WebEncode(Id);
        var encodedFormAction = WebEncode(FormAction);
        var encodedFormMethod = WebEncode(FormMethod);
        var encodedTitle = WebEncode(Title);
        var encodedWarningText = WebEncode(WarningText);
        var encodedBody = WebEncode(Body);
        var encodedConfirmLabel = WebEncode(ConfirmLabel);

        var titleId = $"{encodedId}-title";
        var bodyId = $"{encodedId}-body";

        var sb = new StringBuilder();

        // Native <dialog> opened via .showModal(). The dimming is provided by the
        // ::backdrop pseudo (not a sibling element) — when .showModal() is active
        // the browser makes everything outside the dialog inert, so a sibling
        // backdrop element is unclickable. ::backdrop is owned by the dialog and
        // its clicks bubble to the dialog with e.target === dialog.
        sb.Append($"<dialog id=\"{encodedId}\" class=\"govuk-modal-dialogue__box\" ");
        sb.Append($"aria-labelledby=\"{titleId}\" aria-describedby=\"{bodyId}\" ");
        sb.Append("aria-modal=\"true\" tabindex=\"-1\">");

        // Black header band with right-aligned X close button.
        sb.Append("<div class=\"govuk-modal-dialogue__header\">");
        sb.Append("<button type=\"button\" class=\"govuk-modal-dialogue__close\" ");
        sb.Append("aria-label=\"close\" data-element=\"govuk-modal-dialogue-close\" ");
        sb.Append("data-modal-close>&times;</button>");
        sb.Append("</div>");

        sb.Append("<div class=\"govuk-modal-dialogue__content\">");

        sb.Append($"<h2 class=\"govuk-modal-dialogue__heading govuk-heading-l\" id=\"{titleId}\">");
        sb.Append(encodedTitle);
        sb.Append("</h2>");

        sb.Append($"<form action=\"{encodedFormAction}\" method=\"{encodedFormMethod}\">");
        sb.Append(childHtml);

        if (Destructive)
        {
            sb.Append("<div class=\"govuk-warning-text\">");
            sb.Append("<span class=\"govuk-warning-text__icon\" aria-hidden=\"true\">!</span>");
            sb.Append("<strong class=\"govuk-warning-text__text\">");
            sb.Append("<span class=\"govuk-visually-hidden\">Warning</span>");
            sb.Append(encodedWarningText);
            sb.Append("</strong>");
            sb.Append("</div>");
        }

        sb.Append($"<div class=\"govuk-modal-dialogue__description govuk-body\" id=\"{bodyId}\">");
        sb.Append(encodedBody);
        sb.Append("</div>");

        sb.Append("<div class=\"govuk-button-group\">");

        var confirmClasses = Destructive
            ? "govuk-button govuk-button--warning"
            : "govuk-button";
        sb.Append($"<button type=\"submit\" class=\"{confirmClasses}\" data-module=\"govuk-button\">");
        sb.Append(encodedConfirmLabel);
        sb.Append("</button>");

        sb.Append("<a class=\"govuk-link\" href=\"#\" data-confirm-cancel autofocus>Cancel</a>");

        sb.Append("</div>");

        sb.Append("</form>");
        sb.Append("</div>");
        sb.Append("</dialog>");

        output.Content.SetHtmlContent(sb.ToString());
    }

    private static string WebEncode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
