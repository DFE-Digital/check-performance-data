namespace DfE.CheckPerformanceData.Web.Models.Dev;

// One row in the guided HAT runner: a single human-checkable acceptance item drawn from the
// 03.10 / 03.13 UAT inventory. The model is the single source of truth the view renders from, so
// the runner cannot drift from the inventory. Pass/fail/skip and notes are NOT held here — they
// live client-side in localStorage so the page persists a run without any server-side store.
public sealed record HatItem(
    string Id,
    HatPhase Phase,
    string Title,
    string Expected,
    IReadOnlyList<HatAction> Actions,
    string? SurfaceLink = null);

// One inline action button on a runner row. A POST action posts back to a dev endpoint that
// reuses existing dev logic and redirects to the console; a link action just opens a surface.
public sealed record HatAction(
    string Label,
    string Url,
    HatActionMethod Method = HatActionMethod.Link);

public enum HatActionMethod
{
    Link,
    Post,
}

public enum HatPhase
{
    Phase310,
    Phase313,
}
