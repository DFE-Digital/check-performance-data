using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public interface IQuestionFlowService
{
    Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType);
    JourneyPage? GetPage(QuestionFlowConfig config, string pageId);
    string? GetNextPageId(QuestionFlowConfig config, string pageId, Dictionary<string, QuestionAnswer> answers);
    JourneyPage? GetReachableEvidencePage(QuestionFlowConfig config, Dictionary<string, QuestionAnswer> answers);
    JourneyNavigation? GetNavigationGuard(QuestionFlowConfig config, RequestState journey, string pageId);
    /// <summary>
    /// The CMS key for a Content or EvidenceUpload page. It names the window type, never the window
    /// id, so the guidance can be copied between environments, where the windows have other ids.
    /// </summary>
    string BuildContentKey(JourneyPage page, Dictionary<string, QuestionAnswer> answers, RequestState journey, QuestionFlowConfig config);
    /// <summary>
    /// The CMS blocks for an EvidenceUpload page: one for each ticked option of a contentKey
    /// Checkbox before the page, in config order, so the page shows the guidance for every option
    /// the school chose (#591). With no such Checkbox it is the one <see cref="BuildContentKey"/>.
    /// </summary>
    IReadOnlyList<EvidenceContentBlock> BuildEvidenceContentBlocks(JourneyPage page, Dictionary<string, QuestionAnswer> answers, RequestState journey, QuestionFlowConfig config);
    string ResolveRequestType(QuestionFlowConfig config, RequestState journey);
    string ResolveRequestTypeValue(QuestionFlowConfig config, RequestState journey);
}
