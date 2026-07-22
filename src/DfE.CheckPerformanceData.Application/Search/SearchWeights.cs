namespace DfE.CheckPerformanceData.Application.Search;

/// <summary>
/// Named char constants for the five tsvector ranking weights that drive
/// full-text search across PageNode, PageNodeVersion, and ContentBlock.
/// </summary>
/// <remarks>
/// Runtime source of truth remains the <c>setweight(..., 'X')</c> SQL literals in
/// the three EF Core entity configurations under
/// <c>Persistence/Configurations/</c>:
/// <list type="bullet">
///   <item><c>PageNodeConfiguration.cs</c> — Keywords 'A', Title 'B', Subtitle 'C'</item>
///   <item><c>PageNodeVersionConfiguration.cs</c> — BodyPlainText 'D'</item>
///   <item><c>ContentBlockConfiguration.cs</c> — Keywords 'A', ValuePlainText 'B'</item>
/// </list>
/// The constants below document that runtime SQL and give the Application layer a
/// name to reason about each weight without opening the migration files. The linkage
/// is docs-only rather than compile-time: interpolating these constants into the
/// <c>HasComputedColumnSql</c> literal would turn every future weight edit into an EF
/// Core migration diff, so the constants stay in step with the SQL via a source-file
/// drift-detection unit test (SearchWeightsTests) rather than by construction.
/// See the ranking-contract weight table (PRD §7.1) for the rationale behind each
/// letter.
/// </remarks>
public static class SearchWeights
{
    /// <summary>
    /// Keywords column weight. Highest confidence signal — editor-declared aliases
    /// live here. Applied to <c>PageNode.Keywords</c> and <c>ContentBlock.Keywords</c>.
    /// PRD §7.1.
    /// </summary>
    public const char KeywordsWeight = 'A';

    /// <summary>
    /// Title column weight. Direct label of the page — high confidence but less
    /// specific than editor-declared keywords. Applied to <c>PageNode.Title</c>.
    /// PRD §7.1.
    /// </summary>
    public const char TitleWeight = 'B';

    /// <summary>
    /// Subtitle column weight. Descriptive — useful context but not the primary
    /// label. Applied to <c>PageNode.Subtitle</c>. PRD §7.1.
    /// </summary>
    public const char SubtitleWeight = 'C';

    /// <summary>
    /// Body column weight. Long-form live-version body text; a match here is a
    /// weaker signal than a match in a labelled field. Applied to
    /// <c>PageNodeVersion.BodyPlainText</c>. PRD §7.1.
    /// </summary>
    public const char BodyWeight = 'D';

    /// <summary>
    /// Value (plain text) column weight. The block's actual display text — no
    /// separate title exists, so this sits at the same weight as PageNode.Title.
    /// Applied to <c>ContentBlock.ValuePlainText</c>. PRD §7.1.
    /// </summary>
    public const char ValueWeight = 'B';
}
