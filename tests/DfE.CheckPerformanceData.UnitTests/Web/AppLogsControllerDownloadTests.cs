using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #615 (OWASP A04): the app-log CSV is streamed straight to the response by the controller's
// private StreamedCsvResult, so the no-store header must be set by the result when it executes,
// not by the action. The test drives ExecuteResultAsync against a writable body the way the
// pipeline would.
public sealed class AppLogsControllerDownloadTests
{
    [Fact]
    public async Task Download_streams_the_csv_and_never_caches_it()
    {
        var logs = Substitute.For<IAppLogRepository>();
        logs.StreamAsync(Arg.Any<AppLogQuery>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<AppLogDto>());

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var sut = new AppLogsController(logs, Substitute.For<ISettingService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = sut.Download(null, null, null, null, null);

        await result.ExecuteResultAsync(sut.ControllerContext!);

        Assert.Equal("no-store", httpContext.Response.Headers.CacheControl.ToString());
        Assert.Equal("text/csv; charset=utf-8", httpContext.Response.ContentType);
    }
}
