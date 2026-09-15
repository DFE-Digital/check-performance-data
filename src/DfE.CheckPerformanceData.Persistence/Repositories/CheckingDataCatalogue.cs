using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class CheckingDataCatalogue(PortalDbContext db, TimeProvider clock) : ICheckingDataCatalogue
{
    public async Task<IReadOnlyList<CheckingDataExercise>> GetVisibleAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetLocalNow().DateTime;
        return await db.CheckingWindows.AsNoTracking()
            .SelectMany(w => w.CheckingExercises
                .Where(e => e.TabName != null && e.DataType != null && e.IsEnabled
                    && (e.VisibleFrom == null || e.VisibleFrom <= now)
                    && (e.VisibleUntil == null || e.VisibleUntil > now))
                .Select(e => new { Window = w, Exercise = e }))
            .OrderBy(x => x.Exercise.TabOrder).ThenBy(x => x.Exercise.Id)
            .Select(x => new CheckingDataExercise(x.Exercise.Id, x.Window.Id,
                x.Exercise.Name ?? x.Exercise.TabName!, x.Exercise.Stage ?? string.Empty,
                x.Exercise.TabName!, x.Exercise.TabOrder, x.Exercise.DataType!.Value,
                x.Exercise.ExerciseType, x.Window.KeyStage, x.Exercise.IsEnabled,
                x.Exercise.VisibleFrom, x.Exercise.VisibleUntil, x.Window.StartDate, x.Window.EndDate,
                x.Exercise.StartDate, x.Exercise.EndDate, x.Exercise.ReplacesCheckingExerciseId, x.Exercise.UsesExerciseStorage))
            .ToListAsync(cancellationToken);
    }
}
