using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

// Single place that assembles the context journey conditions inspect, shared by
// the GET-side option filtering (view model builder) and the POST-side
// selection gate (controller) so both always evaluate the same facts.
internal static class JourneyConditionContextFactory
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
