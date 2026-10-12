using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Entities;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// AB#307773: the in-memory twin of the Extensions member written inline in each EF projection
// (a projection cannot call a method). Keep the two in step: order, fields and LAESTAB order.
internal static class CheckingExerciseExtensionMapping
{
    public static List<CheckingExerciseExtensionDto> ToDtos(IEnumerable<CheckingExerciseExtension> extensions) =>
        extensions
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new CheckingExerciseExtensionDto
            {
                Id = x.Id,
                EndDate = x.EndDate,
                CreatedAt = x.CreatedAt,
                CreatedBy = x.CreatedBy,
                RequestsSentAt = x.RequestsSentAt,
                Laestabs = x.Schools.Select(s => s.Laestab).OrderBy(l => l).ToList()
            })
            .ToList();
}
