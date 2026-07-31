using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Web.Controllers;

// Environment-gate guard on the destructive endpoints of the sample-data admin surface.
// Every endpoint returns NotFound() the moment IHostEnvironment.EnvironmentName is not
// on the whitelist, BEFORE touching the gateway or the confirmation-literal check, so a
// stale bookmark or a curl POST from Production / Preproduction / QA cannot execute the
// destructive path even if the caller carries a valid antiforgery token and the exact
// "DELETE" confirm string.
//
// If a future contributor loosens the class-level gate (e.g. to demo on QA), the view
// still hides the Delete-all button because the same predicate is checked in the view.
// This test pins the server side.
public sealed class TestDataControllerEnvironmentGateTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Preproduction")]
    [InlineData("Review")]
    [InlineData("Some-Custom-Env")]
    [InlineData("")]
    public async Task DeleteAllSampleSearchData_OnNonWhitelistedEnvironment_ReturnsNotFound(
        string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var controller = new TestDataController(
            seeder: null!,
            environment: env,
            jobStore: Substitute.For<ISampleSearchDataSeedJobStore>(),
            scopeFactory: Substitute.For<IServiceScopeFactory>(),
            dbContext: null,
            currentUserService: null,
            adminGateway: Substitute.For<ISampleSearchDataGateway>(),
            settings: null);

        // The caller carries a valid typed-confirmation and a valid antiforgery token
        // shape — the env gate must still 404 before any of that matters.
        var result = await controller.DeleteAllSampleSearchData(
            confirmForm: "DELETE",
            confirmQuery: null,
            cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("QA")]
    public async Task DeleteAllSampleSearchData_OnWhitelistedEnvironment_DoesNotShortCircuitToNotFound(
        string environmentName)
    {
        // On a whitelisted env the controller must PROCEED past the env gate. The path
        // downstream of the gate depends on TempData / DbContext / audit-write plumbing
        // this hand-constructed instance doesn't wire, so a NullReferenceException here
        // is fine — it proves execution reached the destructive-path body rather than
        // stopping at NotFound. What must NOT happen is a NotFoundResult.
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var gateway = Substitute.For<ISampleSearchDataGateway>();
        gateway.DeleteAllAsync(Arg.Any<CancellationToken>())
            .Returns(new DeleteCountsResult(0, 0, 0));

        var controller = new TestDataController(
            seeder: null!,
            environment: env,
            jobStore: Substitute.For<ISampleSearchDataSeedJobStore>(),
            scopeFactory: Substitute.For<IServiceScopeFactory>(),
            dbContext: null,
            currentUserService: null,
            adminGateway: gateway,
            settings: null);
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        };

        IActionResult? result = null;
        try
        {
            result = await controller.DeleteAllSampleSearchData(
                confirmForm: "DELETE",
                confirmQuery: null,
                cancellationToken: CancellationToken.None);
        }
        catch (NullReferenceException)
        {
            // Expected — execution reached TempData/audit code the bare controller
            // instance can't service. That IS proof the env gate opened.
            return;
        }

        Assert.IsNotType<NotFoundResult>(result);
    }
}
