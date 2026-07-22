using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Web.Search;

// Concrete for ISearchDebugOptions. Configuration wins if Search:ShowDebug is set
// (bool? so an unset key falls through the ?? rather than binding to false); otherwise
// the environment picks — true in Development, false everywhere else. Registered as a
// Singleton at composition time because both dependencies are singletons and the
// accessor is a pure read.
public sealed class SearchDebugOptions(IConfiguration configuration, IHostEnvironment environment) : ISearchDebugOptions
{
    public bool ShowSearchDebug =>
        configuration.GetValue<bool?>("Search:ShowDebug") ?? environment.IsDevelopment();
}
