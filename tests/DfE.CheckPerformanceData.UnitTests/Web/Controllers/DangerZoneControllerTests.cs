using System.Reflection;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The Danger zone reset endpoint runs the full startup seeding (destructive: wipes change
// requests + checking windows and reseeds). It is admin-role gated AND hard-guarded against
// Production: the POST 404s in prod and never invokes the orchestrator. Outside prod it runs
// the orchestrator once and redirects to /admin with a TempData result banner.
public sealed class DangerZoneControllerTests
{
    private readonly IDevDataSeedingOrchestrator _orchestrator = Substitute.For<IDevDataSeedingOrchestrator>();

    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return env;
    }

    private DangerZoneController CreateSut(string environmentName)
    {
        var sut = new DangerZoneController(_orchestrator, Env(environmentName));
        sut.TempData = new TempDataDictionary(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            Substitute.For<ITempDataProvider>());
        return sut;
    }

    [Fact]
    public void Controller_HasAuthorizeAttribute_WithAdminRole()
    {
        var authorize = typeof(DangerZoneController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    [Fact]
    public void ResetSeedData_Get_ReturnsView()
    {
        var sut = CreateSut("Development");

        var result = sut.ResetSeedData();

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task ResetSeedDataConfirm_InProduction_ReturnsNotFoundAndDoesNotSeed()
    {
        var sut = CreateSut("Production");

        var result = await sut.ResetSeedDataConfirm();

        Assert.IsType<NotFoundResult>(result);
        await _orchestrator.DidNotReceive().RunAsync();
    }

    [Fact]
    public async Task ResetSeedDataConfirm_OutsideProduction_RunsSeedingAndRedirectsToAdmin()
    {
        var sut = CreateSut("QA");

        var result = await sut.ResetSeedDataConfirm();

        await _orchestrator.Received(1).RunAsync();
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin", redirect.Url);
        Assert.True(sut.TempData.ContainsKey("DangerZoneResult"));
    }
}
