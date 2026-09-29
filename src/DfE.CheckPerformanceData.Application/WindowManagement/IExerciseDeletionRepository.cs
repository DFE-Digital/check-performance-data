using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>The database half of <see cref="IDeleteExerciseService"/>.</summary>
public interface IExerciseDeletionRepository
{
    /// <summary>Every change request on the exercise, counted by status. A status with no rows is absent.</summary>
    Task<IReadOnlyDictionary<RequestStatus, int>> CountRequestsByStatusAsync(
        Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the exercise and every row that belongs to it, in one transaction, and sets the
    /// window's dates again from the exercises that are left. Null when the window has no exercise
    /// with that id.
    /// </summary>
    Task<DeletedExercise?> DeleteAsync(Guid windowId, Guid exerciseId, CancellationToken cancellationToken);
}

/// <summary>What the service needs to know about an exercise after its row is gone.</summary>
/// <param name="UsesExerciseStorage">
/// The exercise's files are under its own id, so they can be deleted without touching another
/// exercise's files.
/// </param>
public sealed record DeletedExercise(bool UsesExerciseStorage);
