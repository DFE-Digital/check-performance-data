using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #615 (OWASP A04): every personal-data download must declare Cache-Control: no-store so the
// file — which names people — is never retained by a browser, proxy, or shared device. These pin
// the single shared mechanism every in-scope download action calls, so no download can set the
// header its own way (see NoStoreDownloadSourceGuardTests for the production sweep).
public sealed class HttpResponseExtensionsTests
{
    [Fact]
    public void NoStoreValue_is_exactly_no_store()
        => Assert.Equal("no-store", HttpResponseExtensions.NoStoreValue);

    [Fact]
    public void NoStore_sets_cache_control_to_no_store()
    {
        var response = new DefaultHttpContext().Response;

        response.NoStore();

        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal(HttpResponseExtensions.NoStoreValue, response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void NoStore_replaces_an_existing_cache_control_value_rather_than_appending()
    {
        var response = new DefaultHttpContext().Response;
        response.Headers.CacheControl = "max-age=3600, public";

        response.NoStore();

        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
    }
}
