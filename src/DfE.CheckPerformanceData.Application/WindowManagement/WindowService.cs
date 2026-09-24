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

    public async Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await windowRepository.GetByIdAsync(id, cancellationToken);

    // Start and end dates carry the admin-chosen time-of-day (defaulting to 00:00 / 17:00
    // for new windows, but editable), so both are persisted exactly as supplied.
    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        EnsureDatasetsMatchType(window);
        EnsureExercisesAreValid(window);
        window.DeriveDatesFromExercises();
        window.DeriveKeyStageFromWindowType();
        await windowRepository.UpdateAsync(window, cancellationToken);
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        EnsureDatasetsMatchType(window);
        EnsureExercisesAreValid(window);
        window.DeriveDatesFromExercises();
        window.DeriveKeyStageFromWindowType();
        return await windowRepository.CreateAsync(window, cancellationToken);
    }

    /// <summary>
    /// The last guard before a save. The admin pages validate the same rules and show a form error;
    /// this refuses a caller that skipped them. Every exercise has a tab name, and a window never
    /// has two live exercises of one kind (see <see cref="LiveExercises"/>).
    /// </summary>
    private static void EnsureExercisesAreValid(CheckingWindowDto window)
    {
        if (window.Exercises.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.TabName)) is { } unnamed)
            throw new InvalidOperationException(
                $"Checking exercise {unnamed.Id} ({unnamed.ExerciseType}) has no tab name.");

        if (LiveExercises.FindAnyClash(window.Exercises) is var (first, second))
            throw new InvalidOperationException(
                $"Checking exercises {first.Id} and {second.Id} are both live {first.ExerciseType} exercises " +
                "with overlapping visibility.");
    }

    /// <summary>
    /// Adds the supplier slots a window type needs, and removes supplier slots of another window
    /// type when the type changes (e.g. KS4June -> Post16). Only a results enquiry has supplier
    /// slots now. Every other slot, and every uploaded file, is kept.
    /// </summary>
    /// <remarks>
    /// Every exercise is asked: since #324 the results-enquiry exercise owns one slot per source
    /// file in the results feed, which is what gives an admin somewhere to upload them on a
    /// deployed environment. Pupil data checking and data shares start with no slots; the admin
    /// adds them.
    ///
    /// A window that runs no pupil-data exercise gets no pupil dataset slots and no exercise
    /// invented for it — since #319 the admin chooses the exercises, so a results-enquiry-only
    /// window is a thing an admin can legitimately build, and silently adding pupil data checking
    /// back would undo their choice. The one exception is a window with no exercises at all: that
    /// is a caller which predates the wizard, and it keeps the old shape of one pupil-data exercise
    /// across the whole window.
    /// </remarks>
    private static void EnsureDatasetsMatchType(CheckingWindowDto window)
    {
        if (window.Exercises.Count == 0)
        {
            window.Exercises.Add(new CheckingExerciseDto
            {
                ExerciseType = CheckingExerciseType.PupilData,
                TabName = WindowExercises.DefaultTabName(window.CheckingWindowType, CheckingExerciseType.PupilData),
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = WindowExercises.SortOrderFor(CheckingExerciseType.PupilData)
            });
        }

        foreach (CheckingExerciseDto exercise in window.Exercises)
        {
            // Keep every slot the exercise has, except a supplier slot of another window type.
            // This used to rebuild the list from the defaults alone, which silently dropped every
            // slot an admin added with "Add data file" the moment the window was saved.
            List<CheckingWindowDatasetDto> wanted = exercise.Datasets
                .Where(d => !WindowDatasets.IsStaleSupplierSlot(window.CheckingWindowType, exercise.ExerciseType, d.Name))
                .ToList();

            // Add any supplier slot this window type needs and the exercise does not have yet.
            foreach (CheckingWindowDatasetDto expected in
                     WindowDatasets.DefaultsFor(window.CheckingWindowType, exercise.ExerciseType))
            {
                if (wanted.All(d => d.Name != expected.Name))
                    wanted.Add(expected);
            }

            exercise.Datasets = wanted;
        }
    }
}
