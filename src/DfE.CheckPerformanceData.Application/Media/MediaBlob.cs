namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>A stored media file, opened for reading. The caller disposes it.</summary>
public sealed record MediaBlob(Stream Content, string ContentType, long Length) : IAsyncDisposable, IDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
    public void Dispose() => Content.Dispose();
}
