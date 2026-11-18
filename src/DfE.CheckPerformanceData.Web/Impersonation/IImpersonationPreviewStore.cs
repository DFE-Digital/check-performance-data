namespace DfE.CheckPerformanceData.Web.Impersonation;

/// <summary>Stores temporary impersonation data in this application process only.</summary>
public interface IImpersonationPreviewStore
{
    bool TryGet(string binding, string generation, string key, out byte[]? value);
    void Set(string binding, string generation, string key, byte[] value);
    void Remove(string binding, string generation, string key);
    IReadOnlyCollection<string> GetKeys(string binding, string generation);
    void RemoveSession(string binding, string generation);
    string SaveEvidence(string binding, string generation, Guid windowId, byte[] content);
    byte[]? GetEvidence(string binding, string generation, Guid windowId, string storedName);
    void RemoveEvidence(string binding, string generation, Guid windowId, string storedName);
}
