namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors LayoutViewSourceTests / ContentStagingViewSourceTests):
// reads the Messages nav item view and the admin stylesheet from disk and asserts the Messages
// link is styled white-on-dark like the other admin nav items, without picking up the layout-
// bearing class those items also carry.
public sealed class AdminNavigationViewSourceTests
{
    private const string MessagesBadgeViewPath = "Views/Shared/Components/MessagesBadge/Default.cshtml";
    private const string SiteCssPath = "wwwroot/css/site.css";

    private static string ReadWebSource(string relativePath)
    {
        var webDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(webDir, relativePath));
    }

    [Fact]
    public void MessagesNavItem_CarriesTheWhiteOnDarkClass()
    {
        var source = ReadWebSource(MessagesBadgeViewPath);

        Assert.Contains("class=\"govuk-service-navigation__item messages-nav-item\"", source);
    }

    [Fact]
    public void MessagesNavItem_DoesNotCarryTheAdminLinkClass()
    {
        var source = ReadWebSource(MessagesBadgeViewPath);

        // Regression guard: admin-link-nav-item also sets margin-left:auto and, via the
        // "admin-link-nav-item + sign-in-nav-item" sibling rule in site.css, strips the
        // auto margin off the sign-in item. Messages is the only item ahead of sign-in in
        // the admin nav, so reusing that class would shunt Messages to the right-hand edge
        // of the nav band, next to Sign out, instead of leaving it where it renders today.
        Assert.DoesNotContain("admin-link-nav-item", source);
    }

    [Fact]
    public void AdminNavLinks_AreWhiteOnTheDarkBand()
    {
        var source = ReadWebSource(SiteCssPath);

        // The base, hover and focus rule groups that already carry sign-in-nav-item and
        // admin-link-nav-item must also carry messages-nav-item, so the Messages link picks
        // up the same white-on-#1d1d1d treatment instead of falling through to MoJ
        // Frontend's --govuk-link-colour. Loose regex matches (rather than a fixed selector
        // order) so ordinary reformatting of the selector list doesn't break this test; the
        // base-rule pattern requires the next character to be "," or "{" so it can't be
        // satisfied by the :hover / :focus variants alone.
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(@"\.messages-nav-item \.govuk-service-navigation__link\s*[,{]"),
            source);
        Assert.Contains(".messages-nav-item .govuk-service-navigation__link:hover", source);
        Assert.Contains(".messages-nav-item .govuk-service-navigation__link:focus", source);
    }
}
