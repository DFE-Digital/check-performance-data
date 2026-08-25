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
    // Whitelist — every non-customer-facing environment.
    [InlineData("Development",    true)]
    [InlineData("Review",         true)]
    [InlineData("QA",             true)]
    // Denylist — the only environments that must never render the destructive surface.
    [InlineData("Preproduction",  false)]
    [InlineData("Production",     false)]
    // Hypothetical future tier — off by default; a new whitelist entry is a
    // deliberate opt-in, not silent inheritance.
    [InlineData("Staging",        false)]
    [InlineData("",               false)]
    [InlineData("Some-Custom-Env", false)]
    public void IsSampleDataAdminEnvironment_AllowsEveryNonProductionTier(
        string environmentName, bool expected)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        Assert.Equal(expected, env.IsSampleDataAdminEnvironment());
    }
}
