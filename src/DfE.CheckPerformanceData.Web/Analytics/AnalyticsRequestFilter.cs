namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Decides whether a request should produce a `web_request` event in BigQuery.
/// Wired up as <c>DfeAnalyticsAspNetCoreOptions.RequestFilter</c> in Program.cs.
/// </summary>
/// <remarks>
/// The excluded paths are the agreed list of scanner / bot probes (WordPress, Joomla,
/// Drupal JSON:API, web shells, file managers, GIS endpoints), exposed-configuration
/// probes, technical/API paths we do not serve, and static assets — plus our own
/// health probe. None of these represent a user action, so they are noise in the
/// dataset.
///
/// Matching is on <see cref="HttpRequest.Path"/> (case-insensitive, ordinal) and is
/// deliberately independent of routing: 404s from these paths are re-executed through
/// <c>/Home/NotFound</c>, so by the time the event is sent the request *has* matched an
/// endpoint. Endpoint presence therefore cannot be used to identify unwanted traffic.
/// </remarks>
public static class AnalyticsRequestFilter
{
    private static readonly HashSet<string> ExactPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/.env",                 // exposed configuration
        "/.vscode/sftp.json",    // exposed configuration
        "/api",                  // technical/API
        "/api-docs",             // technical/API
        "/debug",                // technical/API
        "/docs",                 // technical/API
        "/favicon.ico",          // static asset
        "/graphql",              // technical/API
        "/ip",                   // technical/API
        "/openapi.json",         // technical/API
        "/robots.txt",           // bot/technical request
        "/swagger.html",         // technical/API
        "/swagger.json",         // technical/API
        "/xmlrpc.php",           // WordPress
    };

    private static readonly string[] PathPrefixes =
    [
        "/administrator/",                  // Joomla
        "/ALFA_DATA/alfacgiapi",            // web shell
        "/alfacgiapi",                      // web shell
        "/app/jsonapi",                     // JSON:API / Drupal
        "/arcgis/rest",                     // ArcGIS
        "/asset/",                          // file manager
        "/assets/",                         // file manager
        "/backend/jsonapi",                 // JSON:API / Drupal
        "/cms/jsonapi",                     // JSON:API / Drupal
        "/components",                      // Joomla
        "/content/jsonapi",                 // JSON:API / Drupal
        "/css",                             // static asset
        "/dev/impersonate/",                // development / test tooling
        "/drupal/jsonapi",                  // JSON:API / Drupal
        "/en/jsonapi",                      // JSON:API / Drupal
        "/ERENUSE",                         // web shell
        "/filemanager",                     // file manager
        "/file-manager",                    // file manager
        "/geoserver",                       // GeoServer
        "/gis/rest",                        // GIS
        "/index.php",                       // Joomla / PHP
        "/jancox/alfacgiapi",               // web shell
        "/js/",                             // static asset
        "/jsonapi",                         // JSON:API / Drupal
        "/mapping/rest",                    // GIS
        "/MapServer",                       // MapServer
        "/plugins",                         // Joomla
        "/portal/jsonapi",                  // JSON:API / Drupal
        "/portal/sharing/rest",             // ArcGIS Portal
        "/responsive_filemanager",          // file manager
        "/rest/services",                   // GIS
        "/server/rest",                     // GIS
        "/site/jsonapi",                    // JSON:API / Drupal
        "/src/assets/vendor/filemanager",   // file manager
        "/vendor/filemanager",              // file manager
        "/vendors/filemanager",             // file manager
        "/web/jsonapi",                     // JSON:API / Drupal
        "/wp-content",                      // WordPress
        "/wp-json",                         // WordPress
    ];

    private static readonly string[] PathSuffixes =
    [
        "/wp-includes/wlwmanifest.xml",     // WordPress
    ];

    /// <summary>
    /// True when the request should be sent to BigQuery.
    /// </summary>
    public static bool ShouldTrack(HttpContext context) => ShouldTrack(context.Request.Path);

    /// <summary>
    /// True when the path should be sent to BigQuery.
    /// </summary>
    public static bool ShouldTrack(PathString path)
    {
        // Our own liveness/readiness probe, matched on segments so a real page called
        // "/healthchecker" would still be tracked.
        if (path.StartsWithSegments("/healthcheck"))
            return false;

        // A pathless request (e.g. "http://host") is not one of the excluded probes.
        if (!path.HasValue)
            return true;

        var value = path.Value!;

        if (ExactPaths.Contains(value))
            return false;

        foreach (var prefix in PathPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var suffix in PathSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
