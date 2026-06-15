using Testcontainers.Azurite;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    public string ConnectionString => _azurite.GetConnectionString();

    public async Task InitializeAsync() => await _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();
}

[CollectionDefinition(nameof(AzuriteCollection))]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture> { }
