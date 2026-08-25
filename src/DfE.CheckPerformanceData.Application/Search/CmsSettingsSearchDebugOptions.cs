using DfE.CheckPerformanceData.Application.Settings;

namespace DfE.CheckPerformanceData.Application.Search;

// ISearchDebugOptions concrete backed by the CMS settings store (CMS:SearchDebugOn).
// It is the SINGLE knob that gates both:
//   * the <!-- rank: N --> HTML comment rendered on /search (Razor injects the sync
//     accessor and reads ShowSearchDebug in the view), and
//   * the log-level promotion of per-hit rank breakdowns and per-exclusion filter
//     breadcrumbs inside LoggerSearchTelemetry (Debug when off, Information when on).
//
// LIFETIME: Scoped. The interface accessor stays synchronous because Razor views read
// it inline and cannot await. We reach the underlying setting via a blocking async call
// (GetBoolAsync(...).GetAwaiter().GetResult()) — safe here because the concrete is
// per-request, so the DB round-trip fires at most once per HTTP scope. The Lazy<bool>
// pins that to actually-once even if the accessor is read from multiple call sites
// during rendering. Never register this as Singleton: capturing the Scoped
// ISettingService inside a Singleton would leak the request-scoped repository
// across requests.
public sealed class CmsSettingsSearchDebugOptions : ISearchDebugOptions
{
    private readonly Lazy<bool> _showSearchDebug;

    public CmsSettingsSearchDebugOptions(ISettingService settings)
    {
        _showSearchDebug = new Lazy<bool>(() =>
            settings.GetBoolAsync(SettingKeys.SearchDebugOn).GetAwaiter().GetResult());
    }

    public bool ShowSearchDebug => _showSearchDebug.Value;
}
