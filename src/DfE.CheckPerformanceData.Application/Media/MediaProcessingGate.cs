using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// Limits how many images are decoded at the same time in this process. Decoding a large image
/// holds hundreds of megabytes, so parallel uploads, or many first requests for resized copies,
/// could otherwise use all the memory. Work over the limit waits. One instance is shared by the
/// whole process (a singleton), so the limit holds across requests.
/// </summary>
public sealed class MediaProcessingGate
{
    private readonly SemaphoreSlim _places;

    public MediaProcessingGate(IOptions<MediaProcessingOptions> options)
    {
        var limit = options.Value.MaxConcurrentProcessing;
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(options));
        _places = new SemaphoreSlim(limit, limit);
    }

    /// <summary>Waits for a place. Dispose the result to give the place back.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await _places.WaitAsync(ct);
        return new Place(_places);
    }

    // Gives its place back once, however many times it is disposed.
    private sealed class Place(SemaphoreSlim places) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) places.Release();
        }
    }
}
