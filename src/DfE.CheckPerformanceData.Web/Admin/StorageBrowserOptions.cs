namespace DfE.CheckPerformanceData.Web.Admin;

/// <summary>
/// Containers the storage browser must never reach.
/// </summary>
/// <remarks>
/// The storage-admin section grant decides who may open the browser. It says nothing about what
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
}
