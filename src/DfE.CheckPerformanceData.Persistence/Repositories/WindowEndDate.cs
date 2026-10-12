using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <summary>
/// The window's own end date is the latest end any school sees — the latest exercise end, or a
/// later extension end (AB#307773) — and is never typed (#319). Every writer that moves an
/// exercise or extension end re-derives it here, so no two of them can disagree.
/// </summary>
internal static class WindowEndDate
{
    public static async Task RederiveAsync(PortalDbContext db, Guid windowId, CancellationToken ct)
    {
        var exerciseEnd = await db.CheckingExercises.Where(e => e.CheckingWindowId == windowId)
            .Select(e => (DateTime?)e.EndDate).MaxAsync(ct);
        if (exerciseEnd is null) return;   // no exercises: nothing to derive from (ExerciseDeletionRepository keeps the dates)
        var extensionEnd = await db.CheckingExerciseExtensions
            .Where(x => db.CheckingExercises.Any(e => e.Id == x.CheckingExerciseId && e.CheckingWindowId == windowId))
            .Select(x => (DateTime?)x.EndDate).MaxAsync(ct);
        var latest = extensionEnd > exerciseEnd ? extensionEnd.Value : exerciseEnd.Value;
        await db.CheckingWindows.Where(w => w.Id == windowId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.EndDate, latest), ct);
    }
}
