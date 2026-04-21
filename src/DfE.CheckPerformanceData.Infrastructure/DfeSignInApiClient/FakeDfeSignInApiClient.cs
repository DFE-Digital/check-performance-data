using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;

public class FakeDfeSignInApiClient : IDfESignInApiClient
{
    public Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId)
    {
        return Task.FromResult(new OrganisationDto
        {
            Id = "14E9EC3B-5CAC-409F-B629-9711CF4D2734",
            Laestab = "123/4567",
            Name = "Fake School",
            StatutoryHighAge = 16,
            StatutoryLowAge = 11,
            Urn = "987654",
            Address = "123 Fake Road, Fake Town, FT43 9AB"
        })!;
    }
}