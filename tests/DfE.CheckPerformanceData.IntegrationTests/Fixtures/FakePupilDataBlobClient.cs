using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// In-memory substitute for <see cref="IPupilDataBlobClient"/> so repository tests can
/// supply pupil data without a real blob round-trip. A school with no entry returns
/// <c>null</c> (i.e. "no data"). The window type is ignored — the caller supplies already
/// typed records. The checking exercise is part of the key, as it is part of the real blob path
/// (#316), so a caller that asks for the wrong exercise sees nothing here too.
/// </summary>
public sealed class FakePupilDataBlobClient : IPupilDataBlobClient
{
    private readonly Dictionary<(Guid WindowId, CheckingExerciseType Exercise, string Laestab), List<IPupilRecord>>
        _store = new();

    public void SetPupils(Guid windowId, string laestab, IEnumerable<IPupilRecord> pupils)
        => SetPupils(windowId, CheckingExerciseType.PupilData, laestab, pupils);

    public void SetPupils(Guid windowId, CheckingExerciseType exercise, string laestab, IEnumerable<IPupilRecord> pupils)
        => _store[(windowId, exercise, laestab)] = pupils.ToList();

    public Task<IReadOnlyList<IPupilRecord>?> GetPupilsAsync(
        Guid windowId, CheckingExerciseType exercise, string laestab, CheckingWindowType windowType)
        => Task.FromResult(_store.TryGetValue((windowId, exercise, laestab), out var list)
            ? (IReadOnlyList<IPupilRecord>?)list
            : null);

    public Task<bool> HasPupilDataAsync(Guid windowId, CheckingExerciseType exercise, string laestab)
        => Task.FromResult(_store.ContainsKey((windowId, exercise, laestab)));

    public Task<IReadOnlyList<string>> ListSchoolLaestabsAsync(
        Guid windowId, CheckingExerciseType exercise, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(
            _store.Keys.Where(k => k.WindowId == windowId && k.Exercise == exercise)
                .Select(k => LaestabNormaliser.Normalise(k.Laestab))
                .Distinct()
                .ToList());

    public Task UploadPupilsAsync<T>(
        Guid windowId, CheckingExerciseType exercise, string laestab, List<T> pupils) where T : IPupilRecord
    {
        _store[(windowId, exercise, laestab)] = pupils.Cast<IPupilRecord>().ToList();
        return Task.CompletedTask;
    }
}
