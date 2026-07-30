using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class EditAdviceService(
    IRequestRepository requestRepository,
    IQuestionFlowService flowService,
    IJourneyValidationService validationService,
    ICurrentUserService currentUserService,
    IQuestionOptionalityService optionalityService) : IEditAdviceService
{
    public async Task<AmendmentAdvice?> BuildAsync(Guid windowId, string referenceNumber, RequestState journey)
    {
        if (journey.SelectedWhatToChange is null || journey.CheckingWindow is null)
            return null;

        var type = journey.SelectedWhatToChange.Value;
        var urn = long.Parse(currentUserService.OrganisationUrn);

        var request = await requestRepository.GetAmendmentRequestAsync(windowId, urn, referenceNumber);
        if (request is null) return null;

        var config = await flowService.GetConfigAsync(type, journey.CheckingWindow.CheckingWindowType);
        if (config is null) return null;

        var evidenceMessages = request.Status == RequestStatus.InProgress
            ? GetEvidenceMessages(config, journey)
            : [];

        return new AmendmentAdvice
        {
            RequestType = type,
            PupilName = PupilName(journey),
            AdviceText = AdviceText(type),
            EvidenceMessages = evidenceMessages,
            ReasonForRemoval = ResolveReasonForRemoval(type, config, journey),
            ContinueTarget = ResolveTarget(type, request.Status)
        };
    }

    // The removal reason is the request's UseAsRequestType question, so ResolveRequestType
    // returns its selected option label (empty when the reason hasn't been answered yet).
    private string? ResolveReasonForRemoval(WhatToChange type, QuestionFlowConfig config, RequestState journey)
    {
        if (type != WhatToChange.Remove) return null;
        var label = flowService.ResolveRequestType(config, journey);
        return string.IsNullOrEmpty(label) ? null : label;
    }

    private IReadOnlyList<string> GetEvidenceMessages(QuestionFlowConfig config, RequestState journey)
    {
        var evidencePage = flowService.GetReachableEvidencePage(config, journey.QuestionAnswers);
        if (evidencePage is null) return [];

        var ctx = JourneyConditionContextFactory.Create(journey, currentUserService);
        var conditionallyOptional = optionalityService.GetConditionallyOptionalQuestionIds(evidencePage, ctx);
        var result = validationService.ValidateEvidencePage(evidencePage, journey, PupilName(journey), conditionallyOptional);
        return result?.Messages ?? [];
    }

    private static string PupilName(RequestState journey) =>
        journey.SelectedPupil is { } p ? $"{p.Firstname} {p.Surname}".Trim() : string.Empty;

    private static AmendmentContinueTarget ResolveTarget(WhatToChange type, RequestStatus status)
    {
        // Only InProgress and ReadyToSubmit requests reach this screen: the Amendment
        // requests table (GetAmendmentRequestsAsync) filters to those two statuses, so a
        // SubmittedUnCommitted request never gets here (it would fall through as InProgress).
        if (status == RequestStatus.ReadyToSubmit) return new ContinueToSummary();

        return type switch
        {
            WhatToChange.Remove => new ContinueToPage("reason"),
            WhatToChange.Include => new ContinueToPage("evidence"),
            _ => new ContinueToSummary() // Merge (and any other InProgress) goes to the summary
        };
    }

    private static string AdviceText(WhatToChange type) => type switch
    {
        WhatToChange.Remove => "This request is to remove a pupil. If this is not correct, go back and delete the request.",
        WhatToChange.Merge => "This request is to merge pupil records. If this is not correct, go back and delete the request.",
        WhatToChange.Include => "This request is to include a pupil. If this is not correct, go back and delete the request.",
        _ => "If this is not correct, go back and delete the request."
    };
}
