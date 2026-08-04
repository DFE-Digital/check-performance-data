using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Dashboard;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Dashboard;

public class OrganisationLoginRecorderTests
{
    private readonly IOrganisationLoginRepository _repository = Substitute.For<IOrganisationLoginRepository>();

    private OrganisationLoginRecorder CreateSut()
        => new(_repository, NullLogger<OrganisationLoginRecorder>.Instance);

    private static ClaimsIdentity EnrichedIdentity(string? urn, string? laestab, string? name)
    {
        var identity = new ClaimsIdentity("DfeSignIn");
        if (urn is not null) identity.AddClaim(new Claim("organisation_urn", urn));
        if (laestab is not null) identity.AddClaim(new Claim("organisation_laestab", laestab));
        if (name is not null) identity.AddClaim(new Claim("organisation_name", name));
        return identity;
    }

    [Fact]
    public async Task RecordLoginAsync_WithFullClaims_RecordsNormalisedRow()
    {
        await CreateSut().RecordLoginAsync("user-1", EnrichedIdentity("142313", "860/4070", "Kingsmead School"));

        await _repository.Received(1).RecordAsync(
            new OrganisationLoginRecord("user-1", 142313, "8604070", "Kingsmead School"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "860/4070")]      // no URN claim
    [InlineData("not-a-number", "860/4070")]
    [InlineData("142313", null)]        // no laestab claim
    [InlineData("142313", "")]          // empty laestab
    [InlineData("142313", "n/a")]       // laestab with no digits
    public async Task RecordLoginAsync_WithMissingOrInvalidClaims_RecordsNothing(string? urn, string? laestab)
    {
        await CreateSut().RecordLoginAsync("user-1", EnrichedIdentity(urn, laestab, "Some Org"));

        await _repository.DidNotReceive().RecordAsync(
            Arg.Any<OrganisationLoginRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordLoginAsync_WithMissingName_RecordsEmptyName()
    {
        await CreateSut().RecordLoginAsync("user-1", EnrichedIdentity("142313", "8604070", name: null));

        await _repository.Received(1).RecordAsync(
            new OrganisationLoginRecord("user-1", 142313, "8604070", string.Empty),
            Arg.Any<CancellationToken>());
    }

    // A capturing logger because asserting log LEVEL through NSubstitute's ILogger is
    // awkward (generic TState). Levels list is enough — messages are not contract.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    [Fact]
    public async Task RecordLoginAsync_OrgWithoutLaestab_LogsInformationNotWarning()
    {
        var logger = new CapturingLogger<OrganisationLoginRecorder>();
        var sut = new OrganisationLoginRecorder(_repository, logger);

        // An LA: valid URN, no laestab claim — a normal, expected path.
        await sut.RecordLoginAsync("user-1", EnrichedIdentity("142313", laestab: null, "Some LA"));

        Assert.Equal([LogLevel.Information], logger.Levels);
    }

    [Fact]
    public async Task RecordLoginAsync_MalformedUrn_LogsWarning()
    {
        var logger = new CapturingLogger<OrganisationLoginRecorder>();
        var sut = new OrganisationLoginRecorder(_repository, logger);

        await sut.RecordLoginAsync("user-1", EnrichedIdentity("not-a-number", "860/4070", "Some School"));

        Assert.Equal([LogLevel.Warning], logger.Levels);
    }
}
