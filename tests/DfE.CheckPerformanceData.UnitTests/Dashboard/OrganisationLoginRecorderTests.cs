using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Dashboard;
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
}
