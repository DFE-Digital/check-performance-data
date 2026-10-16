using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <summary>
/// The window's own end date is the latest exercise end, never typed (#319). Every writer that
/// moves an exercise end re-derives it here, so no two of them can disagree. Extension ends are
/// left out: the stored end is read by admin pages, the
/// establishment-wide requests page and the deadline fallbacks, none of which is asked for a
/// school; a school's own end is CheckingExerciseDto.EndDateFor(laestab).
/// </summary>
internal static class WindowEndDate
{
    public static async Task RederiveAsync(PortalDbContext db, Guid windowId, CancellationToken ct)
    {
        var exerciseEnd = await db.CheckingExercises.Where(e => e.CheckingWindowId == windowId)
            .Select(e => (DateTime?)e.EndDate).MaxAsync(ct);
        if (exerciseEnd is null) return;   // no exercises: nothing to derive from (ExerciseDeletionRepository keeps the dates)
        await db.CheckingWindows.Where(w => w.Id == windowId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.EndDate, exerciseEnd.Value), ct);
    }
}
