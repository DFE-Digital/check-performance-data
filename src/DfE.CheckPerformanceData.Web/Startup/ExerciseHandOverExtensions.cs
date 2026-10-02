using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Maintenance;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class ExerciseHandOverExtensions
{
    /// <summary>
    /// AB#302158: the automatic exercise hand-over. Binds <see cref="ExerciseHandOverSettings"/>
    /// from the <c>ExerciseHandOver</c> section (code defaults: on, two hours, one day, five
    /// minutes) and runs <see cref="ExerciseHandOverJob"/> in this host. The service it drives
    /// and the lock it takes are registered with the Application and Persistence layers.
    /// </summary>
    public static IServiceCollection AddCpdExerciseHandOver(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExerciseHandOverSettings>(
            configuration.GetSection(ExerciseHandOverSettings.SectionName));
        services.AddHostedService<ExerciseHandOverJob>();
        return services;
    }
}
