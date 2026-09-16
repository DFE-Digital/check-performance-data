namespace DfE.CheckPerformanceData.Application.UnitTests.AdminRequests;

// Static Razor-source assertions on the per-window requests page, same hostless pattern as
// AccessibilityAuditViewTests: read the .cshtml as text and assert on the source.
public sealed class AdminRequestsViewTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string ReadView()
    {
        // Repo root is three levels up from tests/DfE.CheckPerformanceData.UnitTests/AdminRequests/.
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "AdminRequests", "Index.cshtml"));
    }

    [Fact]
    public void Exercise_Filter_Form_Submits_To_The_Pages_Own_Route()
    {
        // The page is routed at admin/windows/{windowId}/requests. The filter form once posted to
        // /admin/windows/requests?windowId=..., which matches no route, so Apply filter was a
        // guaranteed 404 for everyone.
        var view = ReadView();

        Assert.Contains("action=\"/admin/windows/@Model.WindowId/requests\"", view);
        Assert.DoesNotContain("action=\"/admin/windows/requests\"", view);
        Assert.DoesNotContain("name=\"windowId\"", view);
    }
}
