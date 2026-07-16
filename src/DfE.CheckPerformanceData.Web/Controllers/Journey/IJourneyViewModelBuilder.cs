using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// Assembles journey view models (summary and page) from a <see cref="RequestState"/> and its
/// <see cref="QuestionFlowConfig"/>. An interface so consumers outside the journey (e.g. the bulk
/// review pages) can be unit tested without the concrete projection logic.
/// </summary>
public interface IJourneyViewModelBuilder
{
    SummaryViewModel BuildSummaryVm(
        Guid windowId, RequestState journey, QuestionFlowConfig config,
        string? conflictError = null, bool fromBulk = false);

    PageViewModel BuildPageVm(
        Guid windowId,
        JourneyPage page,
        Dictionary<string, QuestionAnswer> answers,
        RequestState journey,
        bool fromSummary,
        ModelStateDictionary modelState,
        QuestionFlowConfig? config = null,
        string? uploadError = null,
        string? atLeastOneError = null);

    PupilSearchViewModel BuildPupilSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config);
}
