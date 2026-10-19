using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// The forwarded-header trust boundary is the foundation of this feature: with either trust
// list non-empty the framework's "is the immediate peer known?" check runs, the ingress is
// not a known peer, and the edge's X-Forwarded-Proto/Host/For are silently discarded — which
// is what made Request.IsHttps false on deployed pods (an http:// redirect_uri and cookies
// without Secure). Both lists must therefore be empty, and that decision must be recorded.
public sealed class ForwardedHeadersTrustTests
{
    [Fact]
    public void AddCpdCoreWeb_ClearsBothTrustLists()
    {
        var options = ResolveOptions();

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
    }

    [Fact]
    public void AddCpdCoreWeb_ForwardsProtoHostAndClientAddress()
    {
        var options = ResolveOptions();

        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedHost));
    }

    private static ForwardedHeadersOptions ResolveOptions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCpdCoreWeb();

        return builder.Services.BuildServiceProvider()
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }
}
