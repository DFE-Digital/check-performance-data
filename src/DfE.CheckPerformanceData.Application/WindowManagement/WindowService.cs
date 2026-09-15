using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public class WindowService(IWindowRepository windowRepository, TimeProvider timeProvider) : IWindowService
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

    public async Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await windowRepository.GetByIdAsync(id, cancellationToken);

    // Start and end dates carry the admin-chosen time-of-day (defaulting to 00:00 / 17:00
    // for new windows, but editable), so both are persisted exactly as supplied.
    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        foreach (var exercise in window.Exercises.Where(e => e.Id == Guid.Empty && e.Datasets.Count == 0))
            exercise.Datasets = WindowDatasets.DefaultsFor(window.CheckingWindowType, exercise.ExerciseType).ToList();
        await windowRepository.UpdateAsync(window, cancellationToken);
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        InitialiseDefinitions(window);
        window.DeriveDatesFromExercises();
        return await windowRepository.CreateAsync(window, cancellationToken);
    }

    // Populate defaults only when creating an unconfigured exercise. Supplied definitions are
    // authoritative, including custom names and any number of file/schema pairs.
    private static void InitialiseDefinitions(CheckingWindowDto window)
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

        foreach (CheckingExerciseDto exercise in window.Exercises)
        {
            // Defaults are initial configuration, never a ceiling on the collection size.
            if (exercise.Datasets.Count == 0)
                exercise.Datasets = WindowDatasets.DefaultsFor(window.CheckingWindowType, exercise.ExerciseType).ToList();
        }
    }
}
