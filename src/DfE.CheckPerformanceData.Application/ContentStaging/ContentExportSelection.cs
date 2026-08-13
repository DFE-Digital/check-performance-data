namespace DfE.CheckPerformanceData.Application.ContentStaging;

// What an administrator asked to export.
//
// PageNodeIds / ContentBlockIds are the stable GUIDs they ticked. Null means "no filter — take
// everything of that kind"; an empty set means "none of them". The distinction matters: it is
// what lets an export ask for the whole environment while still saying something about how much
// history to bring, which an empty set could not express.
//
// Selected pages bring their ancestors along, so the imported hierarchy stays intact.
//
// MaxVersionsPerNode bounds how much version history rides along. Null means "every version",
// which is what a cross-environment migration wants and almost nothing else does.
public sealed record ContentExportSelection(
    IReadOnlySet<Guid>? PageNodeIds = null,
    IReadOnlySet<Guid>? ContentBlockIds = null,
    int? MaxVersionsPerNode = ContentExportSelection.DefaultMaxVersionsPerNode)
{
    /// <summary>
    /// How many versions per page a routine export carries.
    ///
    /// Exports used to include every version of every page, which is fine while a version body
    /// is 20 KB of markup and stops being fine the moment authors embed images: 500 KB of
    /// base64 across five versions of a hundred pages is 250 MB of history attached to a
    /// bundle whose live content is a few megabytes. Five keeps enough recent history to see
    /// what changed lately without carrying the archive.
    /// </summary>
    public const int DefaultMaxVersionsPerNode = 5;
}
