using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// In-memory substitute for <see cref="IPupilDataBlobClient"/> so repository tests can
/// supply pupil data without a real blob round-trip. A school with no entry returns
/// <c>null</c> (i.e. "no data").
/// </summary>
public sealed class FakePupilDataBlobClient : IPupilDataBlobClient
{
    private readonly Dictionary<(Guid WindowId, string Laestab), List<PupilRecord>> _store = new();

    public void SetPupils(Guid windowId, string laestab, IEnumerable<PupilRecord> pupils)
        => _store[(windowId, laestab)] = pupils.ToList();

    public Task<IReadOnlyList<PupilRecord>?> GetPupilsAsync(Guid windowId, string laestab)
        => Task.FromResult(_store.TryGetValue((windowId, laestab), out var list)
            ? (IReadOnlyList<PupilRecord>?)list
            : null);

    public Task<bool> HasPupilDataAsync(Guid windowId, string laestab)
        => Task.FromResult(_store.ContainsKey((windowId, laestab)));

    public Task UploadPupilsAsync(Guid windowId, string laestab, IReadOnlyList<PupilRecord> pupils)
    {
        _store[(windowId, laestab)] = pupils.ToList();
        return Task.CompletedTask;
    }
}
