namespace DfE.CheckPerformanceData.Application.CurrentUser;

public interface ICurrentUserService
{
    string UserId { get; }
    string DisplayName { get; }
    string OrganisationId { get; }
    string OrganisationName { get; }
    string OrganisationUrn { get; }
    string OrganisationLaestab { get; }
    string OrganisationTypeId { get; }
}
