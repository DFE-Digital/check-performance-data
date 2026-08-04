using System.Reflection;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Locks in the write-side surface for search analytics. Two interfaces (sink for events,
// service for messages) in one folder — one owner per table set. Deliberate constraint:
// the sink does NOT expose a messages-purge method; downstream consumers (the retention
// job) call the message service instead. If a messages-purge method appears on the sink,
// the retention job has two ways to delete the same table and the tests will drift out
// of sync.
public sealed class SearchAnalyticsSinkInterfaceTests
{
    [Fact]
    public void ISearchAnalyticsSink_ExposesRecordBatchAndPurgeExpired()
    {
        var t = typeof(ISearchAnalyticsSink);

        var record = t.GetMethod("RecordBatchAsync");
        Assert.NotNull(record);
        Assert.Equal(typeof(Task), record!.ReturnType);
        var recordParams = record.GetParameters();
        Assert.Equal(2, recordParams.Length);
        Assert.Equal(typeof(IReadOnlyList<SearchEventDto>), recordParams[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), recordParams[1].ParameterType);

        var purge = t.GetMethod("PurgeExpiredAsync");
        Assert.NotNull(purge);
        Assert.Equal(typeof(Task<int>), purge!.ReturnType);
        var purgeParams = purge.GetParameters();
        Assert.Equal(2, purgeParams.Length);
        Assert.Equal(typeof(TimeSpan), purgeParams[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), purgeParams[1].ParameterType);
    }

    // Guard: messages-purge lives on ISearchMessageService, NOT the sink. If a
    // planner adds it to the sink surface later the retention job could delete the same
    // rows twice via two different paths — this test guards the boundary.
    [Fact]
    public void ISearchAnalyticsSink_DoesNotExposeMessagesPurge()
    {
        var t = typeof(ISearchAnalyticsSink);
        Assert.Null(t.GetMethod("PurgeExpiredMessagesAsync"));
        Assert.Null(t.GetMethod("PurgeMessagesExpiredAsync"));
    }

    [Fact]
    public void ISearchMessageService_ExposesPurgeExpiredMessages()
    {
        var t = typeof(ISearchMessageService);

        var purge = t.GetMethod("PurgeExpiredMessagesAsync");
        Assert.NotNull(purge);
        Assert.Equal(typeof(Task<int>), purge!.ReturnType);
        var pparams = purge.GetParameters();
        Assert.Equal(2, pparams.Length);
        Assert.Equal(typeof(TimeSpan), pparams[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), pparams[1].ParameterType);
    }

    [Fact]
    public void SearchEventDto_IsSealedRecordWithPositionalConstructor()
    {
        var t = typeof(SearchEventDto);
        Assert.True(t.IsSealed, "SearchEventDto should be sealed");

        // Positional records synthesise a constructor over every property.
        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(ctors);
        var primaryCtor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
        Assert.True(primaryCtor.GetParameters().Length >= 8,
            "SearchEventDto positional ctor should carry every column the sink writes.");
    }

    [Fact]
    public void DbSearchAnalyticsSink_ImplementsISearchAnalyticsSink()
    {
        Assert.True(typeof(ISearchAnalyticsSink).IsAssignableFrom(typeof(DbSearchAnalyticsSink)));
    }

    [Fact]
    public void DbSearchMessageService_ImplementsISearchMessageService()
    {
        Assert.True(typeof(ISearchMessageService).IsAssignableFrom(typeof(DbSearchMessageService)));
    }

    // Prove the DI registrations exist so downstream code that resolves these
    // interfaces via constructor injection can actually stand up. Reads
    // AddPersistenceDependencies through the same code path the Web + Worker hosts use.
    [Fact]
    public void DependencyManager_RegistersSinkAndMessageService_AsScoped()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=noop"
            })
            .Build();

        services.AddPersistenceDependencies(configuration);

        var sinkDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ISearchAnalyticsSink));
        Assert.NotNull(sinkDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, sinkDescriptor!.Lifetime);
        Assert.Equal(typeof(DbSearchAnalyticsSink), sinkDescriptor.ImplementationType);

        var messageDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ISearchMessageService));
        Assert.NotNull(messageDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, messageDescriptor!.Lifetime);
        Assert.Equal(typeof(DbSearchMessageService), messageDescriptor.ImplementationType);
    }
}
