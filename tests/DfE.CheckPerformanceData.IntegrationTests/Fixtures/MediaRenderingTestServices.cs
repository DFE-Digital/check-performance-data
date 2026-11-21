using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

// Content blocks point their media library images at signed copies when they render, so a view
// test that renders a block needs the media services. These give an empty library.
public static class MediaRenderingTestServices
{
    public static IServiceCollection AddEmptyMediaLibrary(this IServiceCollection services)
    {
        var resolver = Substitute.For<IMediaVersionResolver>();
        resolver.ResolveAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ResolvedMedia>());
        services.AddSingleton(resolver);
        services.AddSingleton<IMediaUrlBuilder>(new MediaUrlBuilder(new MediaUrlSigner(new TestKey())));
        return services;
    }

    private sealed class TestKey : IMediaUrlSigningKey
    {
        public byte[] GetKey() => Enumerable.Repeat((byte)7, 32).ToArray();
    }
}
