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
        EnsureNames(window);
        EnsureDatasetsMatchType(window);
        window.DeriveDatesFromExercises();
        await windowRepository.UpdateAsync(window, cancellationToken);
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        EnsureNames(window);
        EnsureDatasetsMatchType(window);
        window.DeriveDatesFromExercises();
        return await windowRepository.CreateAsync(window, cancellationToken);
    }

    public async Task<ExerciseChangeResult> AddExerciseAsync(
        Guid windowId, ExerciseDefinition definition, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowRepository.GetByIdAsync(windowId, cancellationToken);
        if (window is null) return ExerciseChangeResult.Refused("Window not found");

        // A kind exercise still blank from before #466 (or a test/seed) must be named before the
        // duplicate-name check below runs, or a caller could add an exercise called e.g. "Pupil
        // data checking" that the check never compares against.
        EnsureNames(window);

        ExerciseDefinition clean = Trim(definition);
        ExerciseChangeResult? refusal = Validate(window, clean, exceptId: null);
        if (refusal is not null) return refusal;

        window.Exercises.Add(new CheckingExerciseDto
        {
            ExerciseType = null,
            Name = clean.Name,
            TabName = clean.TabName,
            SortOrder = clean.SortOrder,
            StartDate = clean.StartDate,
            EndDate = clean.EndDate,
            Datasets = [CustomExerciseDatasets.Slot()]
        });

        await UpdateAsync(window, cancellationToken);
        return ExerciseChangeResult.Ok();
    }

    public async Task<ExerciseChangeResult> UpdateExerciseAsync(
        Guid windowId, Guid exerciseId, ExerciseDefinition definition, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowRepository.GetByIdAsync(windowId, cancellationToken);
        if (window is null) return ExerciseChangeResult.Refused("Window not found");
        CheckingExerciseDto? target = window.FindExercise(exerciseId);
        if (target is null) return ExerciseChangeResult.Refused("Exercise not found");

        // Same reason as AddExerciseAsync: a still-blank kind exercise elsewhere in the window
        // must be named before the duplicate-name check compares against it.
        EnsureNames(window);

        ExerciseDefinition clean = Trim(definition);
        ExerciseChangeResult? refusal = Validate(window, clean, exceptId: exerciseId);
        if (refusal is not null) return refusal;

        // Kind and slots are deliberately not on ExerciseDefinition: the kind decides the blob
        // prefix and the journeys, and the slots hold uploaded files.
        target.Name = clean.Name;
        target.TabName = clean.TabName;
        target.SortOrder = clean.SortOrder;
        target.StartDate = clean.StartDate;
        target.EndDate = clean.EndDate;

        await UpdateAsync(window, cancellationToken);
        return ExerciseChangeResult.Ok();
    }

    public async Task<ExerciseChangeResult> RemoveExerciseAsync(
        Guid windowId, Guid exerciseId, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowRepository.GetByIdAsync(windowId, cancellationToken);
        if (window is null) return ExerciseChangeResult.Refused("Window not found");
        CheckingExerciseDto? target = window.FindExercise(exerciseId);
        if (target is null) return ExerciseChangeResult.Refused("Exercise not found");

        // The outer dates are derived from the exercises, so a window with none has no dates.
        if (window.Exercises.Count == 1)
            return ExerciseChangeResult.Refused("A window must keep at least one exercise");

        // A request is stamped with the exercise it was made under (ChangeRequest.CheckingExerciseId,
        // FK SetNull). Removing the exercise would orphan the stamp and take the request out of
        // every per-exercise sweep. Slice 1 does not touch blobs either way.
        if (await windowRepository.HasChangeRequestsAsync(exerciseId, cancellationToken))
            return ExerciseChangeResult.Refused("This exercise has change requests and cannot be removed");

        window.Exercises.Remove(target);

        await UpdateAsync(window, cancellationToken);
        return ExerciseChangeResult.Ok();
    }

    public async Task<ExerciseChangeResult> SetExercisesAsync(
        Guid windowId, IReadOnlyCollection<string> selectedNames, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowRepository.GetByIdAsync(windowId, cancellationToken);
        if (window is null) return ExerciseChangeResult.Refused("Window not found");

        // Same comparer as Validate() below: a name is the same exercise regardless of case or
        // stray whitespace either side of it.
        HashSet<string> selected = selectedNames
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<CheckingExerciseDto> kept = window.Exercises
            .Where(e => selected.Contains(e.Name.Trim()))
            .ToList();
        List<CheckingExerciseDto> added = WindowExercises.DefaultsFor(window.CheckingWindowType)
            .Where(t => selected.Contains(t.Name)
                        && window.Exercises.All(e => !string.Equals(e.Name.Trim(), t.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.ToDto(window.StartDate, window.EndDate))
            .ToList();

        if (kept.Count + added.Count == 0)
            return ExerciseChangeResult.Refused("Select at least one checking exercise");

        // A removed exercise is stamped on the change requests made under it (FK SetNull), so
        // removing it would take those requests out of every per-exercise sweep — the same rule
        // RemoveExerciseAsync enforces, applied here to every exercise the tick list drops at once.
        foreach (CheckingExerciseDto dropped in window.Exercises.Except(kept))
        {
            if (await windowRepository.HasChangeRequestsAsync(dropped.Id, cancellationToken))
                return ExerciseChangeResult.Refused($"{dropped.Name} has change requests and cannot be removed");
        }

        window.Exercises = kept.Concat(added).OrderBy(e => e.SortOrder).ToList();

        await UpdateAsync(window, cancellationToken);
        return ExerciseChangeResult.Ok();
    }

    private static ExerciseDefinition Trim(ExerciseDefinition d) =>
        // The `?` guards are load-bearing, not defensive style: model binding can hand a null into
        // a non-nullable record parameter when the controller action never checked ModelState.
        d with { Name = d.Name?.Trim() ?? string.Empty, TabName = d.TabName?.Trim() ?? string.Empty };

    private static ExerciseChangeResult? Validate(CheckingWindowDto window, ExerciseDefinition d, Guid? exceptId)
    {
        if (d.Name.Length == 0) return ExerciseChangeResult.Refused("Enter a name");
        if (d.TabName.Length == 0) return ExerciseChangeResult.Refused("Enter a tab name");
        if (d.Name.Length > ExerciseDefinition.MaxNameLength)
            return ExerciseChangeResult.Refused($"Name must be {ExerciseDefinition.MaxNameLength} characters or less");
        if (d.TabName.Length > ExerciseDefinition.MaxTabNameLength)
            return ExerciseChangeResult.Refused($"Tab name must be {ExerciseDefinition.MaxTabNameLength} characters or less");
        if (d.EndDate < d.StartDate) return ExerciseChangeResult.Refused("End date can not occur before the start date");
        bool taken = window.Exercises.Any(e =>
            e.Id != exceptId && string.Equals(e.Name.Trim(), d.Name, StringComparison.OrdinalIgnoreCase));
        if (taken) return ExerciseChangeResult.Refused("An exercise with that name already exists in this window");
        return null;
    }

    /// <summary>
    /// A kind exercise created by a caller that never named it (the wizard before #466, a test, a
    /// seed) takes its default name. A display-only exercise is named by the admin or by its
    /// template: for Add/Update, Validate() has already refused a blank one, but a display-only
    /// exercise reaching UpdateAsync(window) directly (not through UpdateExerciseAsync) with a
    /// blank name is persisted blank — the column is NOT NULL DEFAULT '', not refused.
    /// </summary>
    private static void EnsureNames(CheckingWindowDto window)
    {
        foreach (CheckingExerciseDto exercise in window.Exercises)
        {
            if (exercise.ExerciseType is not { } kind) continue;
            if (string.IsNullOrWhiteSpace(exercise.Name)) exercise.Name = CheckingExerciseNames.NameFor(kind);
            if (string.IsNullOrWhiteSpace(exercise.TabName)) exercise.TabName = CheckingExerciseNames.TabNameFor(kind);
        }
    }

    /// <summary>
    /// A window's dataset set is decided by its type and by which exercises it runs, so changing
    /// the type (e.g. KS4June -> Post16) adds or removes dataset slots on every exercise. Files
    /// already uploaded to a slot that survives are kept.
    /// </summary>
    /// <remarks>
    /// Every exercise is asked, not just pupil data: since #324 the results-enquiry exercise owns
    /// one slot per source file in the results feed, which is what gives an admin somewhere to
    /// upload them on a deployed environment.
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
                Name = CheckingExerciseNames.NameFor(CheckingExerciseType.PupilData),
                TabName = CheckingExerciseNames.TabNameFor(CheckingExerciseType.PupilData),
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = WindowExercises.SortOrderFor(CheckingExerciseType.PupilData)
            });
        }

        foreach (CheckingExerciseDto exercise in window.Exercises)
        {
            // A display-only exercise's slots are the admin's, not the type's (#466).
            if (exercise.ExerciseType is null) continue;

            List<CheckingWindowDatasetDto> wanted = [];

            foreach (CheckingWindowDatasetDto expected in
                     WindowDatasets.DefaultsFor(window.CheckingWindowType, exercise.ExerciseType.Value))
            {
                CheckingWindowDatasetDto? existing = exercise.Datasets.SingleOrDefault(d => d.Name == expected.Name);
                wanted.Add(existing ?? expected);
            }

            exercise.Datasets = wanted;
        }
    }
}
