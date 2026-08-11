using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class AnalyticsRequestFilterTests
{
    // The agreed exclusion list, exactly as supplied: exact matches.
    [Theory]
    [InlineData("/.env")]
    [InlineData("/.vscode/sftp.json")]
    [InlineData("/api")]
    [InlineData("/api-docs")]
    [InlineData("/debug")]
    [InlineData("/docs")]
    [InlineData("/favicon.ico")]
    [InlineData("/graphql")]
    [InlineData("/ip")]
    [InlineData("/openapi.json")]
    [InlineData("/robots.txt")]
    [InlineData("/swagger.html")]
    [InlineData("/swagger.json")]
    [InlineData("/xmlrpc.php")]
    public void Excludes_exact_paths(string path) =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    // /feedback-link (AB#286387 R20) emits its own feedback_clicked event and then
    // redirects to /contact; without this exclusion the redirect hop would double-count
    // the click as a web_request page view too.
    [Fact]
    public void Excludes_the_feedback_link_tracking_redirect() =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString("/feedback-link")));

    // /client-events (AB#286387 R18/R19/R23) is the client-side beacon endpoint; it emits
    // its own typed event (help_details_expanded / external_link_clicked /
    // evidence_file_selected), so tracking the POST itself as a web_request would be noise.
    [Fact]
    public void Excludes_the_client_events_beacon_endpoint() =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString("/client-events")));

    // Prefix matches — tested with a trailing segment so the prefix rule is what fires.
    [Theory]
    [InlineData("/administrator/index.php")]
    [InlineData("/ALFA_DATA/alfacgiapi/perl.alfa")]
    [InlineData("/alfacgiapi/perl.alfa")]
    [InlineData("/app/jsonapi/node/page")]
    [InlineData("/arcgis/rest/info")]
    [InlineData("/asset/upload.php")]
    [InlineData("/assets/govuk-frontend.css")]
    [InlineData("/backend/jsonapi/node")]
    [InlineData("/cms/jsonapi/node")]
    [InlineData("/components/com_users")]
    [InlineData("/content/jsonapi/node")]
    [InlineData("/css/site.css")]
    [InlineData("/dev/impersonate/12345")]
    [InlineData("/drupal/jsonapi/node")]
    [InlineData("/en/jsonapi/node")]
    [InlineData("/ERENUSE/shell.php")]
    [InlineData("/filemanager/dialog.php")]
    [InlineData("/file-manager/dialog.php")]
    [InlineData("/geoserver/web")]
    [InlineData("/gis/rest/services")]
    [InlineData("/index.php")]
    [InlineData("/jancox/alfacgiapi/perl.alfa")]
    [InlineData("/js/site.js")]
    [InlineData("/jsonapi/node/page")]
    [InlineData("/mapping/rest/services")]
    [InlineData("/MapServer/export")]
    [InlineData("/plugins/system/debug")]
    [InlineData("/portal/jsonapi/node")]
    [InlineData("/portal/sharing/rest/info")]
    [InlineData("/responsive_filemanager/filemanager/dialog.php")]
    [InlineData("/rest/services/info")]
    [InlineData("/server/rest/services")]
    [InlineData("/site/jsonapi/node")]
    [InlineData("/src/assets/vendor/filemanager/dialog.php")]
    [InlineData("/vendor/filemanager/dialog.php")]
    [InlineData("/vendors/filemanager/dialog.php")]
    [InlineData("/web/jsonapi/node")]
    [InlineData("/wp-content/plugins/x.php")]
    [InlineData("/wp-json/wp/v2/users")]
    public void Excludes_prefix_paths(string path) =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    [Theory]
    [InlineData("/wp-includes/wlwmanifest.xml")]
    [InlineData("/blog/wp-includes/wlwmanifest.xml")]
    [InlineData("/2019/wp-includes/wlwmanifest.xml")]
    public void Excludes_suffix_paths(string path) =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    [Theory]
    [InlineData("/healthcheck")]
    [InlineData("/healthcheck/ready")]
    public void Excludes_health_probes(string path) =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    // Scanners vary the casing of probe paths; matching is ordinal-insensitive.
    [Theory]
    [InlineData("/WP-CONTENT/plugins/x.php")]
    [InlineData("/XMLRPC.PHP")]
    [InlineData("/Alfacgiapi/perl.alfa")]
    public void Exclusions_are_case_insensitive(string path) =>
        Assert.False(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    [Theory]
    [InlineData("/")]
    [InlineData("/landing")]
    [InlineData("/journey/page/select-pupil")]
    [InlineData("/pupils/suggestions")]
    [InlineData("/countries/suggestions")]
    [InlineData("/admin/settings")]
    [InlineData("/Home/NotFound")]
    [InlineData("/apidocs")]          // no hyphen — not the excluded "/api-docs"
    [InlineData("/apiary")]           // exact rule must not swallow other "/api…" pages
    [InlineData("/healthchecker")]    // segment match, not a raw prefix
    public void Tracks_application_paths(string path) =>
        Assert.True(AnalyticsRequestFilter.ShouldTrack(new PathString(path)));

    [Fact]
    public void Tracks_request_with_no_path() =>
        Assert.True(AnalyticsRequestFilter.ShouldTrack(new PathString(null)));

    [Fact]
    public void ShouldTrack_reads_the_path_from_the_HttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/wp-json/wp/v2/users";

        Assert.False(AnalyticsRequestFilter.ShouldTrack(context));

        context.Request.Path = "/landing";

        Assert.True(AnalyticsRequestFilter.ShouldTrack(context));
    }
}
