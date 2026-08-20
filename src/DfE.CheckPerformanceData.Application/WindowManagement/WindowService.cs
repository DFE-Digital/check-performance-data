using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowService(IWindowRepository windowRepository, TimeProvider timeProvider): IWindowService
{
    public async Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetLocalNow();
        List<CheckingWindowDto> windows = await windowRepository.GetAllWindowsAsync(cancellationToken);

        foreach (CheckingWindowDto window in windows)
        {
            window.IsOpen = window.StartDate <= now.DateTime && now.DateTime <= window.EndDate;
        }

        return new PageResult
        {
            Windows = windows
        };
    }

    public async Task<CheckingWindowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await windowRepository.GetByIdAsync(id, cancellationToken);

    // Start and end dates carry the admin-chosen time-of-day (defaulting to 00:00 / 17:00
    // for new windows, but editable), so both are persisted exactly as supplied.
    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        EnsureDatasetsMatchType(window);
        window.DeriveDatesFromExercises();
        await windowRepository.UpdateAsync(window, cancellationToken);
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        EnsureDatasetsMatchType(window);
        window.DeriveDatesFromExercises();
        return await windowRepository.CreateAsync(window, cancellationToken);
    }

    /// <summary>
    /// A window's dataset set is decided by its type, so changing the type (e.g. KS4June -> Post16)
    /// adds or removes dataset slots. Files already uploaded to a slot that survives are kept.
    /// The slots hang off the pupil-data exercise.
    /// </summary>
    /// <remarks>
    /// A window that runs no pupil-data exercise gets no dataset slots and no exercise invented for
    /// it — since #319 the admin chooses the exercises, so a results-enquiry-only window is a thing
    /// an admin can legitimately build, and silently adding pupil data checking back would undo
    /// their choice. The one exception is a window with no exercises at all: that is a caller which
    /// predates the wizard, and it keeps the old shape of one pupil-data exercise across the whole
    /// window.
    ///
    /// The results-enquiry exercise gets no dataset slots yet — its six-file ingress is #324.
    /// </remarks>
    private static void EnsureDatasetsMatchType(CheckingWindowDto window)
    {
        if (window.Exercises.Count == 0)
        {
            window.Exercises.Add(new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.PupilData,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = WindowExercises.SortOrderFor(CheckingExerciseType.PupilData)
            });
        }

        CheckingExerciseDto? pupilData = window.FindExercise(CheckingExerciseType.PupilData);

        if (pupilData is null)
        {
            return;
        }

        List<CheckingWindowDatasetDto> wanted = [];

        foreach (CheckingWindowDatasetDto expected in WindowDatasets.DefaultsFor(window.CheckingWindowType))
        {
            CheckingWindowDatasetDto? existing = pupilData.Datasets.SingleOrDefault(d => d.Name == expected.Name);
            wanted.Add(existing ?? expected);
        }

        pupilData.Datasets = wanted;
    }
}
