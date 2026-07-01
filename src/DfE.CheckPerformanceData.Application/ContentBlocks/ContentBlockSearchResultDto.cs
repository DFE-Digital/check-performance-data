namespace DfE.CheckPerformanceData.Application.ContentBlocks;

/// <summary>A content-block match for the help search results page.</summary>
public sealed record ContentBlockSearchResultDto
{
    public required string Key { get; init; }

    /// <summary>The page (and section anchor, where applicable) that renders this block.</summary>
    public required string Url { get; init; }

    public required string PageTitle { get; init; }

    /// <summary>Safe snippet HTML: tag-stripped plain text, the matched term wrapped in &lt;mark&gt;.</summary>
    public required string SnippetHtml { get; init; }
}
