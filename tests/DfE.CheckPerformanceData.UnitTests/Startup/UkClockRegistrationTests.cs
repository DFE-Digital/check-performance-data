using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Startup;

// #535: both hosts must hand out the UK clock. The web host does not call
// AddInfrastructureDependencies or AddRulesProvider, so its TimeProvider comes from
// AddCpdBlobStorage and AddCpdSession; the worker's comes from AddRulesProvider. A fourth
// registration of the system clock anywhere would put that host back on the container's zone,
// which is what the source guards at the bottom are for.
public sealed class UkClockRegistrationTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AzureStorage"] = "UseDevelopmentStorage=true"
        })
        .Build();

    // The last registration is the one the container resolves.
    private static object? RegisteredClock(IServiceCollection services) =>
        services.Last(d => d.ServiceType == typeof(TimeProvider)).ImplementationInstance;

    private static IWebHostEnvironment Development()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return environment;
    }

    [Fact]
    public void The_web_hosts_blob_storage_registration_hands_out_the_UK_clock()
    {
        var services = new ServiceCollection();

        services.AddCpdBlobStorage(Configuration);

        Assert.Same(UkTimeProvider.Instance, RegisteredClock(services));
    }

    [Fact]
    public void The_web_hosts_session_registration_hands_out_the_UK_clock()
    {
        var services = new ServiceCollection();

        services.AddCpdSession(Configuration, Development());

        Assert.Same(UkTimeProvider.Instance, RegisteredClock(services));
    }

    [Fact]
    public void The_workers_rules_provider_registration_hands_out_the_UK_clock()
    {
        var services = new ServiceCollection();

        services.AddRulesProvider(Configuration);

        Assert.Same(UkTimeProvider.Instance, RegisteredClock(services));
    }

    [Fact]
    public void A_test_host_that_registered_its_own_clock_first_keeps_it()
    {
        var own = new OwnClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(own);

        services.AddCpdSession(Configuration, Development());
        services.AddCpdBlobStorage(Configuration);

        Assert.Same(own, Assert.Single(services, d => d.ServiceType == typeof(TimeProvider)).ImplementationInstance);
    }

    [Fact]
    public void No_host_registers_the_system_clock()
    {
        var registersSystemClock = new Regex(@"AddSingleton(<TimeProvider>)?\(\s*TimeProvider\.System\s*\)");

        Assert.Empty(Offenders(registersSystemClock, [], "*.cs"));
    }

    [Fact]
    public void Nothing_in_src_reads_the_hosts_own_local_time()
    {
        // These read the container's zone directly and so bypass the registered clock. The dev
        // seed is allowed: its margins are a day wide, and it is not a gate.
        var readsHostLocalTime = new Regex(@"\bDateTime\.(Now|Today)\b|\bDateTimeOffset\.Now\b");

        Assert.Empty(Offenders(readsHostLocalTime, ["SeedCheckingWindows.cs"], "*.cs", "*.cshtml"));
    }

    private static List<string> Offenders(Regex pattern, string[] allowedFileNames, params string[] searchPatterns) =>
        SourceFiles(searchPatterns)
            .Where(file => !allowedFileNames.Contains(Path.GetFileName(file)))
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .ToList();

    private static IEnumerable<string> SourceFiles(params string[] searchPatterns)
    {
        char s = Path.DirectorySeparatorChar;
        string[] skipped = [$"{s}bin{s}", $"{s}obj{s}", $"{s}node_modules{s}"];

        return searchPatterns
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), p, SearchOption.AllDirectories))
            .Where(file => !skipped.Any(part => file.Contains(part)));
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    private sealed class OwnClock : TimeProvider;
}
