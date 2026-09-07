using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// AB#298317 review: SignOutLink.For is the one place that decides where "Sign out" goes (real DfE
// auth → DfE sign-out; impersonation → cookie clear). _Layout and Check your pupil data already
// use it; the admin layout re-derived the same answer inline, so a future change to sign-out would
// have silently diverged for /admin.
public sealed class AdminLayoutRenderTests
{
    private static string ReadAdminLayout()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_AdminLayout.cshtml"));
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void Admin_layout_resolves_sign_out_through_SignOutLink()
    {
        var layout = ReadAdminLayout();

        Assert.Contains("SignOutLink.For(Context, Url)", layout);
        // Neither branch of the old inline ternary may survive.
        Assert.DoesNotContain("\"/dev/impersonate/clear\"", layout);
        Assert.DoesNotContain("Url.Action(\"DfeSignOut\"", layout);
    }
}
