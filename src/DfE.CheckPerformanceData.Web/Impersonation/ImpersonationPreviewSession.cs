using Microsoft.AspNetCore.Http;
using System.Diagnostics.CodeAnalysis;

namespace DfE.CheckPerformanceData.Web.Impersonation;

/// <summary>
/// Routes journey state keys to process memory while preserving the normal session for
/// unrelated session data.
/// </summary>
public sealed class ImpersonationPreviewSession(
    ISession inner,
    IImpersonationPreviewStore previewStore,
    string binding,
    string generation) : ISession
{
    private const string RequestStatePrefix = "request_";
    private static readonly string[] OtherPreviewKeys = ["SelectedWindowId", "SelectedWindowType"];

    public bool IsAvailable => inner.IsAvailable;
    public string Id => inner.Id;

    public IEnumerable<string> Keys => inner.Keys
        .Concat(previewStore.GetKeys(binding, generation).Where(IsPreviewKey))
        .Distinct(StringComparer.Ordinal);

    public void Clear()
    {
        inner.Clear();
        previewStore.RemoveSession(binding, generation);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);

    public void Remove(string key)
    {
        if (IsPreviewKey(key)) previewStore.Remove(binding, generation, key);
        else inner.Remove(key);
    }

    public void Set(string key, byte[] value)
    {
        if (IsPreviewKey(key)) previewStore.Set(binding, generation, key, value);
        else inner.Set(key, value);
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value)
    {
        if (IsPreviewKey(key)) return previewStore.TryGet(binding, generation, key, out value);
        return inner.TryGetValue(key, out value);
    }

    private static bool IsPreviewKey(string key) =>
        key.StartsWith(RequestStatePrefix, StringComparison.Ordinal)
        || OtherPreviewKeys.Contains(key, StringComparer.Ordinal);
}
