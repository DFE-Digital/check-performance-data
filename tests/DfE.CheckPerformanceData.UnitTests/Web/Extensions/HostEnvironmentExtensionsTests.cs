using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Web.Extensions;

// Environment-name whitelist for the sample-data admin surface. Pinned as a set of
// InlineData so every environment tier the terraform config declares (see
// terraform/application/config/*.yml) is enumerated here.
//
// Deliberate: QA / Preproduction / Production must ALL evaluate to false. If a
// contributor renames Review (or adds a new tier) the compile passes but this test
// forces them to look at the list.
public sealed class HostEnvironmentExtensionsTests
{
    [Theory]
    // Whitelist — the developer's local stack + the shared Azure test env.
    [InlineData("Development",    true)]
    [InlineData("QA",             true)]
    // Denylist — Review is per-PR ephemeral (should not accidentally wipe review
    // data); Preproduction / Production are the obvious ones.
    [InlineData("Review",         false)]
    [InlineData("Preproduction",  false)]
    [InlineData("Production",     false)]
    // Hypothetical future tier — off by default; a new whitelist entry is a
    // deliberate opt-in, not silent inheritance.
    [InlineData("Staging",        false)]
    [InlineData("",               false)]
    [InlineData("Some-Custom-Env", false)]
    public void IsSampleDataAdminEnvironment_HonoursTheDevelopmentAndQaWhitelist(
        string environmentName, bool expected)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        Assert.Equal(expected, env.IsSampleDataAdminEnvironment());
    }
}
