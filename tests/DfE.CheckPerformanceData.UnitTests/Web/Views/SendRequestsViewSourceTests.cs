using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// #536: the send page shows requests that are waiting for the Rules Engine, separately from the
// ones it will send, and says so when they are all that is left.
public sealed class SendRequestsViewSourceTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", "SendRequests.cshtml"));

    [Fact]
    public void The_summary_lists_waiting_requests_as_their_own_row()
    {
        var view = View();
        Assert.Contains("Requests waiting for the Rules Engine", view);
        Assert.Contains("@Model.RequestsWaiting", view);
    }

    [Fact]
    public void The_page_explains_when_only_waiting_requests_are_left()
    {
        var view = View();
        Assert.Contains("else if (Model.OnlyWaiting)", view);
        Assert.Contains("There is nothing to send yet.", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
