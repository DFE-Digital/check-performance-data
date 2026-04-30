using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace DfE.CheckPerformanceData.Application.Common;

public interface IHtmlRenderingService
{
    string? RenderHtml(string? content);

    /// <summary>
    /// Strips HTML tags from <em>already-sanitised</em> HTML to produce plain text suitable
    /// for FTS indexing and ts_headline snippet generation.
    ///
    /// Safe ONLY when the input has already passed through <see cref="RenderHtml"/> (or is otherwise
    /// guaranteed tag-safe) — the regex does not parse nested structure and assumes no adversarial
    /// tag malformations remain.
    /// </summary>
    string StripTagsToPlainText(string? sanitisedHtml);
}

public sealed partial class HtmlRenderingService : IHtmlRenderingService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    // GOV.UK Frontend components rely on `class`, `aria-*`, `role`, and `data-module` attributes
    // for styling (govuk-warning-text, govuk-notification-banner, etc.) and behaviour
    // (data-module triggers component JS). Ensure those pass the sanitizer; XSS vectors
    // (<script>, on*, javascript: URLs) remain blocked by Ganss defaults.
    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedAttributes.Add("class");
        s.AllowedAttributes.Add("role");
        s.AllowedAttributes.Add("data-module");
        s.AllowedAttributes.Add("aria-hidden");
        s.AllowedAttributes.Add("aria-label");
        s.AllowedAttributes.Add("aria-labelledby");
        s.AllowedAttributes.Add("aria-describedby");
        s.AllowedAttributes.Add("aria-expanded");
        s.AllowedAttributes.Add("aria-controls");
        s.AllowedAttributes.Add("aria-current");
        return s;
    }

    public string? RenderHtml(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var html = ContainsHtmlTags().IsMatch(content)
            ? content
            : Markdown.ToHtml(content, MarkdownPipeline);

        return Sanitizer.Sanitize(html);
    }

    public string StripTagsToPlainText(string? sanitisedHtml)
    {
        if (string.IsNullOrEmpty(sanitisedHtml))
        {
            return string.Empty;
        }

        var stripped = HtmlTagPattern().Replace(sanitisedHtml, " ");
        var collapsed = WhitespaceCollapse().Replace(stripped, " ");
        return collapsed.Trim();
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapse();

    [GeneratedRegex(@"<\s*[a-z][a-z0-9]*[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsHtmlTags();
}
