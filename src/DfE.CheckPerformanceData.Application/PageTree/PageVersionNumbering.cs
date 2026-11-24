namespace DfE.CheckPerformanceData.Application.PageTree;

/// <summary>
/// The number that the history, the edit screens and the published and draft labels show for a
/// page version. It is the version's own number (VersionId): the next free number for the page
/// when the version is added, unique for the page (a unique index guards it) and never changed.
/// An editor's draft shows the number it publishes as, because publishing keeps the version.
/// A version added above a draft, for example by the embedded image move, takes the next free
/// number, so two versions never show the same number and no number moves later.
/// </summary>
public static class PageVersionNumbering
{
    public static string Label(PageNodeVersionDto version) => version.VersionId.ToString();
}
