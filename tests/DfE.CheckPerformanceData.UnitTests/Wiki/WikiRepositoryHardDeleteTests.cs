using System.Reflection;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Persistence.Repositories;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

// Repository-level cascade behaviour is exercised end-to-end against a real Postgres
// container in the integration test suite (HardDeleteAsync against a seeded subtree,
// audit-row assertions, rollback assertions). EF Core's IQueryable + ChangeTracker
// surface is impractical to substitute meaningfully at the unit boundary, so this
// shell pins only the public-surface declarations — the integration tests own the
// behavioural depth.
public sealed class WikiRepositoryHardDeleteTests
{
    // --- IWikiRepository_Declares_HardDeleteAsync ---

    [Fact]
    public void IWikiRepository_Declares_HardDeleteAsync()
    {
        var method = typeof(IWikiRepository).GetMethod(nameof(IWikiRepository.HardDeleteAsync));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    // --- IWikiRepository_Declares_GetIsDeletedAsync ---

    [Fact]
    public void IWikiRepository_Declares_GetIsDeletedAsync()
    {
        var method = typeof(IWikiRepository).GetMethod(nameof(IWikiRepository.GetIsDeletedAsync));
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    // --- WikiRepository_Implements_HardDeleteAsync ---

    [Fact]
    public void WikiRepository_Implements_HardDeleteAsync()
    {
        var method = typeof(WikiRepository).GetMethod(
            nameof(IWikiRepository.HardDeleteAsync),
            BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
    }
}
