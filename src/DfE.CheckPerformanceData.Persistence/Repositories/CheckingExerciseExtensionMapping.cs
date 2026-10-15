using System.Linq.Expressions;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Entities;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// AB#307773: the one shape an extension takes on a window DTO. Every EF read uses Projection
// inside its nested Select (EF Core inlines a static expression there), and the in-memory path
// compiles the same expression, so the fields and the LAESTAB order cannot drift between reads.
// Keep the ordering beside it: extensions are listed oldest first, ties by id.
internal static class CheckingExerciseExtensionMapping
{
    public static readonly Expression<Func<CheckingExerciseExtension, CheckingExerciseExtensionDto>> Projection =
        x => new CheckingExerciseExtensionDto
        {
            Id = x.Id,
            EndDate = x.EndDate,
            CreatedAt = x.CreatedAt,
            CreatedBy = x.CreatedBy,
            RequestsSentAt = x.RequestsSentAt,
            Laestabs = x.Schools.Select(s => s.Laestab).OrderBy(l => l).ToList()
        };

    private static readonly Func<CheckingExerciseExtension, CheckingExerciseExtensionDto> Compiled = Projection.Compile();

    public static List<CheckingExerciseExtensionDto> ToDtos(IEnumerable<CheckingExerciseExtension> extensions) =>
        extensions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(Compiled).ToList();
}
