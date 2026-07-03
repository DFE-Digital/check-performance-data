using Testcontainers.Azurite;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        // The Azure Storage SDK sends a newer x-ms-version than azurite:latest recognises,
        // so azurite rejects every request with 400 InvalidHeaderValue unless the version
        // check is skipped. This mirrors the --skipApiVersionCheck flag the docker-compose
        // azurite already uses. Appended to the module's default host-binding command.
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public string ConnectionString => _azurite.GetConnectionString();

    public async Task InitializeAsync() => await _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();
}

[CollectionDefinition(nameof(AzuriteCollection))]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture> { }
