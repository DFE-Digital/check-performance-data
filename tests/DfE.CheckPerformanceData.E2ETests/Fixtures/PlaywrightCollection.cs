namespace DfE.CheckPerformanceData.E2ETests.Fixtures;

// Two separate collection definitions over the same fixture type. xUnit instantiates a
// DISTINCT PlaywrightFixture per definition, so the "E2E" (browser) and "Http" (pure-HTTP)
// collections each get their own readiness probe, impersonation cookie and seed. That
// isolation is what lets the two collections run in parallel (xUnit parallelises across
// collections by default): a pure-HTTP test temporarily impersonating the unprivileged or
// admin principal writes only its own fixture's cookie, never the browser collection's.
[CollectionDefinition("E2E")]
public sealed class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }

[CollectionDefinition("Http")]
public sealed class HttpCollection : ICollectionFixture<PlaywrightFixture> { }
