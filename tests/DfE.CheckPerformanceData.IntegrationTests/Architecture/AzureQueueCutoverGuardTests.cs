using System.Reflection;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.IntegrationTests.Architecture;

// The Azure Storage Queues SDK was removed outright in the Postgres cutover. This guard fails if
// it is reintroduced into the queue path, so the hard cutover cannot silently regress.
public sealed class AzureQueueCutoverGuardTests
{
    // The Web assembly (owns the queue admin surface) and the Worker assembly (owns the
    // consumers) are the two that must never depend on the Azure queue SDK again.
    public static IEnumerable<object[]> QueuePathAssemblies()
    {
        yield return new object[] { typeof(QueueAdminController).Assembly };
        yield return new object[] { typeof(ZendeskConsumer).Assembly };
    }

    [Theory]
    [MemberData(nameof(QueuePathAssemblies))]
    public void QueuePathAssembly_DoesNotReferenceAzureStorageQueues(Assembly assembly)
    {
        var referencesAzureQueues = assembly
            .GetReferencedAssemblies()
            .Any(a => a.Name is not null
                && a.Name.Contains("Azure.Storage.Queues", StringComparison.OrdinalIgnoreCase));

        Assert.False(referencesAzureQueues,
            $"{assembly.GetName().Name} references Azure.Storage.Queues. The queue path is Postgres-only; " +
            "do not reintroduce the Azure Storage Queues SDK.");
    }

    [Theory]
    [MemberData(nameof(QueuePathAssemblies))]
    public void QueuePathAssembly_ExposesNoAzureQueueClientTypes(Assembly assembly)
    {
        // A direct guard against the specific client types named in the cutover criteria, in case
        // a future reference is added transitively but a type is surfaced in this assembly.
        string[] bannedTypeNames = { "QueueServiceClient", "QueueClient" };

        var offending = SafeGetTypes(assembly)
            .Where(t => t.FullName is not null
                && t.FullName.StartsWith("Azure.Storage.Queues", StringComparison.Ordinal)
                && bannedTypeNames.Contains(t.Name))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offending.Count == 0,
            $"{assembly.GetName().Name} surfaces Azure queue client types: {string.Join(", ", offending)}");
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
