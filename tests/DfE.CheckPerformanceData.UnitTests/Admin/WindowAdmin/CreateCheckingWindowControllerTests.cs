using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// The wizard keeps its answers in session. Once the window exists they must go, or the next
// "New window" opens pre-filled with the last window's answers.
public class CreateCheckingWindowControllerTests
{
    [Fact]
    public async Task Creating_the_window_discards_the_draft()
    {
        var session = SessionWithDraft(CompleteDraft());
        var windowService = Substitute.For<IWindowService>();
        windowService.CreateAsync(Arg.Any<CheckingWindowDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CheckingWindowDto>());

        var blobService = Substitute.For<BlobServiceClient>();
        var controller = new CreateCheckingWindowController(
            NullLogger<CreateCheckingWindowController>.Instance,
            windowService,
            new Dictionary<string, BlobServiceClient> { ["app"] = blobService })
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };

        var result = await controller.Post(CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        session.Received(1).Remove("CheckingWindowDraft");
    }

    [Fact]
    public async Task An_invalid_draft_is_kept_so_the_admin_can_finish_it()
    {
        var draft = CompleteDraft();
        draft.Exercises[0].EndDate = null;
        var session = SessionWithDraft(draft);

        var controller = new CreateCheckingWindowController(
            NullLogger<CreateCheckingWindowController>.Instance,
            Substitute.For<IWindowService>(),
            new Dictionary<string, BlobServiceClient>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            }
        };

        await controller.Post(CancellationToken.None);

        session.DidNotReceive().Remove(Arg.Any<string>());
    }

    private static CheckingWindowDraft CompleteDraft() => new()
    {
        Title = "Dave's KS4 June",
        CheckingWindowType = CheckingWindowType.KS4June,
        Exercises =
        [
            new ExerciseDraft
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate = DateTime.UtcNow.Date.AddDays(30),
                SortOrder = 0
            }
        ]
    };

    private static ISession SessionWithDraft(CheckingWindowDraft draft)
    {
        var session = Substitute.For<ISession>();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft));
        session.TryGetValue("CheckingWindowDraft", out Arg.Any<byte[]?>())
            .Returns(call =>
            {
                call[1] = bytes;
                return true;
            });
        return session;
    }
}
