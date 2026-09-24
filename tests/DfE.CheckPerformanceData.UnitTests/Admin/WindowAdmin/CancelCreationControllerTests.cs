using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// The "Do you want to delete this window?" page posts action=delete or action=continue.
public class CancelCreationControllerTests
{
    [Fact]
    public void Delete_discards_the_draft_and_returns_to_admin()
    {
        var session = SessionWithDraft(new CheckingWindowDraft { Title = "Dave's KS4 June" });
        var controller = Controller(session);

        var result = controller.Submit("delete");

        session.Received(1).Remove("CheckingWindowDraft");
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Admin", redirect.ControllerName);
    }

    [Fact]
    public void Continue_keeps_the_draft_and_resumes_at_the_next_unanswered_step()
    {
        var session = SessionWithDraft(new CheckingWindowDraft
        {
            Title = "Dave's KS4 June",
            CheckingWindowType = CheckingWindowType.KS4June
        });
        var controller = Controller(session);

        var result = controller.Submit("continue");

        session.DidNotReceive().Remove(Arg.Any<string>());
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/Exercises/New", redirect.Url);
    }

    [Fact]
    public void Continue_with_no_draft_returns_to_admin()
    {
        var controller = Controller(Substitute.For<ISession>());

        var result = controller.Submit("continue");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Admin", redirect.ControllerName);
    }

    private static CancelCreationController Controller(ISession session)
    {
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Any<UrlActionContext>())
            .Returns(call => $"/{call.Arg<UrlActionContext>().Controller}/{call.Arg<UrlActionContext>().Action}");

        return new CancelCreationController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { Session = session }
            },
            Url = url
        };
    }

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
