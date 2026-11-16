namespace DfE.CheckPerformanceData.Web.Navigation;

/// <summary>
/// A section of the school-facing service navigation (#854). The layout marks the item for the
/// active section with the GOV.UK active state.
/// </summary>
public enum ServiceNavSection
{
    Home,
    Pupils,
    AmendmentRequests,

    /// <summary>
    /// A journey page. It is not a nav item. It resolves to <see cref="AmendmentRequests"/> when
    /// the school came from Edit on Amendment requests, and to <see cref="Pupils"/> otherwise.
    /// </summary>
    Journey,
}
