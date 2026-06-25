namespace DfE.CheckPerformanceData.Web.Seeding;

// Runs the full development-data seeding sequence (DB seed → pupil blobs → question-flow
// blobs) as a single unit. Called from Program.cs at startup (when seeding is configured)
// and on demand from the admin Danger zone "Reset seed data" action. Having one entry point
// keeps the orchestration — including the dev-only Azurite failure tolerance — in one place.
public interface IDevDataSeedingOrchestrator
{
    Task RunAsync();
}
