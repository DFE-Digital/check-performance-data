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
        string? conflictError = null, string? conflictErrorLink = null, bool fromBulk = false, bool fromEdit = false);

    PageViewModel BuildPageVm(
        Guid windowId,
        JourneyPage page,
        Dictionary<string, QuestionAnswer> answers,
        RequestState journey,
        bool fromSummary,
        ModelStateDictionary modelState,
        QuestionFlowConfig? config = null,
        string? uploadError = null,
        string? atLeastOneError = null,
        /// <summary>AB#296648: the selected result's grade scale, for a GradeSelect question. Resolved
        /// by the caller because the lookup is async. Null when the page has no grade picker, or when
        /// the QAN is absent from the reference data.</summary>
        Application.ResultsEnquiry.GradeReference? gradeReference = null);

    PupilSearchViewModel BuildPupilSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config);

    /// <summary>
    /// AB#296648: the "which of {pupil}'s results is incorrect?" page. The pupil's results are passed
    /// in rather than fetched here because reading them is async and the builder is not.
    /// </summary>
    ResultSearchViewModel BuildResultSearchVm(
        Guid windowId, string pageId, JourneyPage page, RequestState journey, QuestionFlowConfig config,
        IReadOnlyList<Application.ResultsEnquiry.StudentResultRecord> availableResults);
}
