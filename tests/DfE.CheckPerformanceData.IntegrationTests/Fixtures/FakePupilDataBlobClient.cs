using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// In-memory substitute for <see cref="IPupilDataBlobClient"/> so repository tests can
/// supply pupil data without a real blob round-trip. A school with no entry returns
/// <c>null</c> (i.e. "no data"). The window type is ignored — the caller supplies already
/// typed records.
/// </summary>
public sealed class FakePupilDataBlobClient : IPupilDataBlobClient
{
    private readonly Dictionary<(Guid WindowId, string Laestab), List<IPupilRecord>> _store = new();

    public void SetPupils(Guid windowId, string laestab, IEnumerable<IPupilRecord> pupils)
        => _store[(windowId, laestab)] = pupils.ToList();

    public Task<IReadOnlyList<IPupilRecord>?> GetPupilsAsync(Guid windowId, string laestab, CheckingWindowType windowType)
        => Task.FromResult(_store.TryGetValue((windowId, laestab), out var list)
            ? (IReadOnlyList<IPupilRecord>?)list
            : null);

    public Task<bool> HasPupilDataAsync(Guid windowId, string laestab)
        => Task.FromResult(_store.ContainsKey((windowId, laestab)));

    public Task<IReadOnlyList<string>> ListSchoolLaestabsAsync(Guid windowId)
        => Task.FromResult<IReadOnlyList<string>>(
            _store.Keys.Where(k => k.WindowId == windowId)
                .Select(k => k.Laestab.Replace("/", string.Empty))
                .Distinct()
                .ToList());

    public Task UploadPupilsAsync<T>(Guid windowId, string laestab, List<T> pupils) where T : IPupilRecord
    {
        _store[(windowId, laestab)] = pupils.Cast<IPupilRecord>().ToList();
        return Task.CompletedTask;
    }
}
