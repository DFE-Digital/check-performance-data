using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public string UserId => "test-user";
    public string DisplayName => "Test User";
    public string Email => "test@test.com";
    public string OrganisationId => "5760D65B-1AAD-4E89-98DB-6A0ACC424042";
    public string Ukprn => "10003384";
    public string OrganisationName => "Test School";
    public string OrganisationUrn => "142313";
    public string OrganisationLaestab => "860/4070";
    public string OrganisationTypeId => "11";
}
