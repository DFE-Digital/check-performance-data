using DfE.CheckPerformanceData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

public sealed class DependencyManagerTests
{
    [Fact]
    public void AddDfeSignInAuthentication_WhenSectionMissing_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddDfeSignInAuthentication(config, Substitute.For<IHostEnvironment>()));

        Assert.Contains("DfeSignIn", ex.Message);
    }
}
