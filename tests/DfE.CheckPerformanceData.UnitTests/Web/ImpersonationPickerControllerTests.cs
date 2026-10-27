using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationPickerControllerTests
{
    [Fact]
    public async Task Search_result_values_start_the_selected_establishment_session()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"impersonation-picker-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(contentRoot, "Data", "Impersonation");
        Directory.CreateDirectory(dataDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "EstablishmentDetails.csv"),
                "\"URN\",\"LA (code)\",\"EstablishmentNumber\",\"EstablishmentName\",\"StatutoryLowAge\",\"StatutoryHighAge\"\n" +
                "\"876543\",\"123\",\"0456\",\"Example Secondary School\",3,19\n");

            var environment = Substitute.For<IWebHostEnvironment>();
            environment.ContentRootPath.Returns(contentRoot);
            var directory = new EstablishmentDirectory(environment);

            var byName = Assert.Single(directory.Search("Example Secondary"));
            var byUrn = Assert.Single(directory.Search("876543"));
            Assert.Equal(byName, byUrn);
            Assert.Equal("1230456", byName.Laestab);

            var original = Substitute.For<ICurrentUserService>();
            original.UserId.Returns("original-admin");
            original.OrganisationLaestab.Returns("1000001");
            original.OrganisationUrn.Returns("100001");

            var store = Substitute.For<IImpersonationSessionStore>();
            store.GetAsync("test-binding", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<ImpersonationSessionState?>(null));
            store.SetAsync(Arg.Any<string>(), Arg.Any<ImpersonationSessionState>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            var session = new ImpersonationSessionService(
                Substitute.For<IHttpContextAccessor>(),
                original,
                store,
                new EphemeralDataProtectionProvider(),
                Substitute.For<ICheckYourPupilDataRepository>());
            await session.InitialiseAsync("test-binding", CancellationToken.None);

            var audit = Substitute.For<IImpersonationAuditWriter>();
            var controller = new ImpersonationController(session, audit, original, directory)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, "original-admin"),
                            new Claim(ClaimTypes.Role, ImpersonationAccessPolicy.AdminRole)
                        ], "Test"))
                    }
                }
            };

            var actionResult = await controller.Start(new ImpersonationViewModel
            {
                Laestab = byName.Laestab,
                Urn = byName.Urn,
                LowestAge = byName.LowestAge,
                HighestAge = byName.HighestAge
            });

            var redirect = Assert.IsType<RedirectToActionResult>(actionResult);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal("LandingPage", redirect.ControllerName);
            Assert.True(session.IsImpersonating);
            Assert.Equal("1230456", session.OrganisationLaestab);
            Assert.Equal("876543", session.OrganisationUrn);
            Assert.Equal(3, session.LowestAge);
            Assert.Equal(19, session.HighestAge);
            await audit.Received(1).RecordAsync(
                Arg.Is<ImpersonationAuditRecord>(record =>
                    record.UserId == "original-admin" && record.Laestab == "1230456" && record.Urn == "876543"),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
