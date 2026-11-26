using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;

namespace DfE.CheckPerformanceData.Application.Journey;

// Single place that assembles the context journey conditions inspect, shared by
// the GET-side option filtering, the POST-side selection gate, and evidence
// optionality so all evaluate the same facts.
public static class JourneyConditionContextFactory
{
    public static JourneyConditionContext Create(
        RequestState journey,
        ICurrentUserService currentUser,
        IEstablishmentViewContext? viewContext = null) => new()
    {
        Journey = journey,
        User = new JourneyUserContext
        {
            OrganisationUrn = viewContext?.OrganisationUrn ?? currentUser.OrganisationUrn,
            OrganisationId = currentUser.OrganisationId,
            OrganisationName = currentUser.OrganisationName,
            // The impersonation directory does not contain a verified organisation type.
            // Fail closed for type-gated options instead of borrowing the administrator's type.
            OrganisationTypeId = viewContext?.IsImpersonating == true
                ? string.Empty
                : currentUser.OrganisationTypeId,
            OrganisationLaestab = viewContext?.OrganisationLaestab ?? currentUser.OrganisationLaestab
        }
    };
}
