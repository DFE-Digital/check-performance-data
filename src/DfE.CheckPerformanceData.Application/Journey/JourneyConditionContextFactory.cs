using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.Application.Journey;

// Single place that assembles the context journey conditions inspect, shared by
// the GET-side option filtering, the POST-side selection gate, and evidence
// optionality so all evaluate the same facts.
public static class JourneyConditionContextFactory
{
    public static JourneyConditionContext Create(RequestState journey, ICurrentUserService currentUser) => new()
    {
        Journey = journey,
        User = new JourneyUserContext
        {
            OrganisationUrn = currentUser.OrganisationUrn,
            OrganisationId = currentUser.OrganisationId,
            OrganisationName = currentUser.OrganisationName,
            OrganisationTypeId = currentUser.OrganisationTypeId
        }
    };
}
