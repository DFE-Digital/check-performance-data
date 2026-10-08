namespace DfE.CheckPerformanceData.Web.Admin;

/// <summary>
/// Containers the storage browser must never reach.
/// </summary>
/// <remarks>
/// The storage-browser section grant decides who may open the browser. It says nothing about what
/// the browser may touch, so every container in the account was reachable — including the one
/// holding the Data Protection keyring, which protects authentication cookies, session state and
/// antiforgery tokens. Reading it allows those to be decrypted, replacing it allows them to be
/// forged, and deleting it invalidates every one of them at once.
///
/// A secret has no business being served by the application that holds it, whoever is asking.
/// Configurable rather than hard-coded so a future secret container is covered by a setting
/// instead of a release.
/// </remarks>
public sealed class StorageBrowserOptions
{
    public const string SectionName = "StorageBrowser";

    /// <summary>
    /// Container names the browser refuses to list, read, write or delete. Matched
    /// case-insensitively.
    /// </summary>
    public string[] ProtectedContainers { get; set; } = ["data-protection-keys"];

    /// <summary>
    /// Hard ceiling on one stage-block request, used by the action's <c>[RequestSizeLimit]</c>.
    /// Kestrel's own limit is disabled app-wide, so without this the action would accept an
    /// unbounded body. A compile-time constant because attributes cannot read configuration;
    /// <see cref="ChunkBytes"/> must fit under it.
    /// </summary>
    public const long ChunkRequestCeilingBytes = 32L * 1024 * 1024;

    /// <summary>
    /// What the plain form can carry in one request: the cluster's ingress controller caps a
    /// request body at 50 MB (teacher-services-cloud, ingress_controller.tf). This number is
    /// shown in the hint; the cap itself is not ours to change.
    /// </summary>
    public const int SingleUploadHintMegabytes = 50;

    /// <summary>
    /// #568: upload in parts from the browser, staged as block-blob blocks. On everywhere,
    /// production included: it is an admin function and production is where the product owner
    /// needs it. Only the bounds below are configurable; there is no feature switch.
    /// </summary>
    /// <remarks>Bytes per part. 8 MiB keeps each request well under the 50 MB ingress cap and the
    /// Front Door timeouts on a slow uplink, at 256 requests for a 2 GiB file.</remarks>
    public int ChunkBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Largest file a chunked upload may commit: 2 GiB, the product owner's number.</summary>
    public long MaxUploadBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Highest part count a single upload may stage; bounds the index before any block is written.</summary>
    public int MaxBlocks => (int)Math.Ceiling(MaxUploadBytes / (double)ChunkBytes);

    /// <summary>GOV.UK style size for copy: whole units, no space, MB below a gigabyte.</summary>
    public static string FormatSize(long bytes)
    {
        const long mb = 1024L * 1024;
        const long gb = mb * 1024;
        if (bytes >= gb)
        {
            var g = bytes / (double)gb;
            return g % 1 == 0 ? $"{g:0}GB" : $"{g:0.#}GB";
        }
        var m = bytes / (double)mb;
        return m % 1 == 0 ? $"{m:0}MB" : $"{m:0.#}MB";
    }
}
