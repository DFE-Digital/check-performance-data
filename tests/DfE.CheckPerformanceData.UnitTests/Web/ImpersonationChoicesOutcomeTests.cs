using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationChoicesOutcomeTests
{
    private const string Laestab = "1230456";
    private const string Urn = "876543";
    private const string Binding = "test-binding";

    private readonly Guid _windowId = Guid.NewGuid();
    private readonly Guid _exerciseId = Guid.NewGuid();

    [Theory]
    [InlineData("succeeded", 1)]
    [InlineData("SUCCEEDED", 1)]
    [InlineData("failed", 0)]
    [InlineData("cancelled", 0)]
    public async Task Choices_ListsAnExerciseOnlyWhenItsSummaryHasTheSucceededOutcome(string outcome, int listed)
    {
        var controller = await ControllerAsync(new IngressSummaryDocument
        {
            Outcome = outcome,
            Files =
            [
                new IngressSummaryFile
                {
                    DatasetName = "pupils",
                    Laestabs =
                    [
                        new IngressSummaryLaestab
                        {
                            Laestab = Laestab,
                            Totals = new IngressSummaryCounts { RowsRead = 2, RowsImported = 2 }
                        }
                    ]
                }
            ]
        });

        var view = Assert.IsType<ViewResult>(await controller.Choices());

        var model = Assert.IsType<ImpersonationChoicesViewModel>(view.Model);
        Assert.Equal(listed, model.Exercises.Count);
    }

    private async Task<ImpersonationController> ControllerAsync(IngressSummaryDocument summary)
    {
        var windows = Substitute.For<IWindowRepository>();
        windows.GetAllWindowsAsync(Arg.Any<CancellationToken>()).Returns(new List<CheckingWindowDto>
        {
            new()
            {
                Id = _windowId, Title = "A window", StartDate = DateTime.Today, EndDate = DateTime.Today,
                KeyStage = default, CheckingWindowType = default,
                Exercises =
                [
                    new CheckingExerciseDto
                    {
                        Id = _exerciseId, ExerciseType = CheckingExerciseType.PupilData,
                        StartDate = DateTime.Today, EndDate = DateTime.Today, Name = "Pupil data"
                    }
                ]
            }
        });
        var summaries = Substitute.For<IIngressSummaryStore>();
        summaries.GetAsync(_windowId, _exerciseId, Arg.Any<CancellationToken>()).Returns(summary);

        var original = Substitute.For<ICurrentUserService>();
        var store = Substitute.For<IImpersonationSessionStore>();
        store.GetAsync(Binding, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<ImpersonationSessionState?>(new ImpersonationSessionState("generation", null,
                new EstablishmentSelection(Laestab, Urn, 3, 19))));
        store.SetAsync(Arg.Any<string>(), Arg.Any<ImpersonationSessionState>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var session = new ImpersonationSessionService(Substitute.For<IHttpContextAccessor>(), original, store,
            new EphemeralDataProtectionProvider(), Substitute.For<ICheckYourPupilDataRepository>());
        await session.InitialiseAsync(Binding, CancellationToken.None);

        return new ImpersonationController(session, Substitute.For<IImpersonationAuditWriter>(), original,
            new EstablishmentDirectory(), windows, summaries, Substitute.For<ILogger<ImpersonationController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.Role, ImpersonationAccessPolicy.AdminRole)], "Test"))
                }
            }
        };
    }
}
