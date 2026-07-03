using System.Security.Claims;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ClaimsEnrichment;

public sealed class ClaimsEnrichmentServiceTests
{
    [Fact]
    public void ClaimsEnrichmentService_IsSealed()
    {
        Assert.True(
            typeof(ClaimsEnrichmentService).IsSealed,
            "ClaimsEnrichmentService must be sealed per project convention (CONVENTIONS.md).");
    }

    [Fact]
    public async Task EnrichAsync_DoesNotThrow_When_Organisation_Laestab_Is_Null()
    {
        // Some DfE Sign-in organisations (URN present, but no localAuthority element in the
        // API response) leave OrganisationDto.Laestab null. Handing null to the Claim
        // constructor throws ArgumentNullException, which surfaces as a generic
        // "remote authentication failed" and drops the user on the no-access page. Enrichment
        // must complete instead, with an empty laestab claim.
        var apiClient = Substitute.For<IDfESignInApiClient>();
        apiClient.GetUserRolesAsync("org-1", "user-1")
            .Returns([new RoleDto { Code = "school-user" }]);
        apiClient.GetOrganisationAsync("user-1", "org-1")
            .Returns(new OrganisationDto
            {
                Id = "org-1",
                Urn = "123456",
                Name = "Test School"
                // Laestab deliberately left null
            });

        var service = new ClaimsEnrichmentService(apiClient, NullLogger<ClaimsEnrichmentService>.Instance);

        var result = await service.EnrichAsync(BuildPrincipal("user-1", "org-1"));

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result!.FindFirst("organisation_laestab")?.Value);
        Assert.Contains(result.Claims, c => c.Type == ClaimTypes.Role && c.Value == "school-user");
    }

    private static ClaimsPrincipal BuildPrincipal(string userId, string orgId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("organisation", $"{{\"id\":\"{orgId}\"}}")
        ], "test");
        return new ClaimsPrincipal(identity);
    }
}
