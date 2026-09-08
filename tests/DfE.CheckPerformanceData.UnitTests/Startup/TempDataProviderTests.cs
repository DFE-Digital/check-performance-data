using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// Guards where flash messages are stored.
//
// MVC's default ITempDataProvider is CookieTempDataProvider: every TempData value is
// serialised, encrypted and handed to the browser, which then presents it back on the next
// request. That makes a flash message a request header, and request headers are budgeted by
// the reverse proxy in front of the app, not by Kestrel. The deployed ingress refuses a
// request whose headers exceed its buffer with a 400 the app never sees and cannot explain —
// and because the cookie is presented on every subsequent request, the failure sticks until
// the user clears their cookies. A signed-in user already carries a chunked ~4 KB
// authentication cookie, so a few kilobytes of banner text is enough to cross the line.
//
// The session store is Postgres-backed and shared across every pod
// (AddCpdSessionStore), so keeping TempData server-side costs nothing in reliability and
// keeps the value out of the request entirely.
public class TempDataProviderTests
{
    [Fact]
    public void AddCpdCoreWeb_KeepsTempDataServerSide()
    {
        var provider = ResolveTempDataProvider();

        Assert.IsType<SessionStateTempDataProvider>(provider);
    }

    [Fact]
    public void AddCpdCoreWeb_DoesNotStoreTempDataInACookie()
    {
        var provider = ResolveTempDataProvider();

        Assert.IsNotType<CookieTempDataProvider>(provider);
    }

    private static ITempDataProvider ResolveTempDataProvider()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddCpdCoreWeb();

        // Session is registered by AddCpdSessionStore in the real host, which needs a database.
        // The provider only needs ISession to exist on the request, so an in-memory backing is
        // enough to resolve it here.
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();

        return builder.Services.BuildServiceProvider().GetRequiredService<ITempDataProvider>();
    }
}
