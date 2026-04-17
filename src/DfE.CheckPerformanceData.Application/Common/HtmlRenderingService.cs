using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace DfE.CheckPerformanceData.Application.Common;

public interface IHtmlRenderingService
{
    string? RenderHtml(string? content);
}

public sealed partial class HtmlRenderingService : IHtmlRenderingService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    public string? RenderHtml(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var html = ContainsHtmlTags().IsMatch(content)
            ? content
            : Markdown.ToHtml(content, MarkdownPipeline);

        return Sanitizer.Sanitize(html);
    }

    [GeneratedRegex(@"<\s*[a-z][a-z0-9]*[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsHtmlTags();
}
