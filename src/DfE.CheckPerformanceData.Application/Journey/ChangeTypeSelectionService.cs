using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class ChangeTypeSelectionService(
    IQuestionFlowService flowService,
    IAnalyticsService analytics) : IChangeTypeSelectionService
{
    /// <inheritdoc cref="IChangeTypeSelectionService.GetChoiceAsync(CheckingWindowType)" />
    public async Task<ChangeTypeChoice> GetChoiceAsync(CheckingWindowType checkingWindowType)
    {
        var available = await flowService.GetPupilDataChangesAsync(checkingWindowType);
        if (available.Count == 0)
            throw new InvalidOperationException(
                $"The {checkingWindowType} window type offers no pupil data change type. It needs at least one flow file.");

        return new ChangeTypeChoice(available);
    }

    /// <inheritdoc cref="IChangeTypeSelectionService.StartJourneyAsync(RequestState, CheckingWindowDto, WhatToChange)" />
    public async Task<JourneyStart> StartJourneyAsync(RequestState journey, CheckingWindowDto window, WhatToChange change)
    {
        var available = await flowService.GetPupilDataChangesAsync(window.CheckingWindowType);
        if (!available.Contains(change))
            return JourneyStart.Refused;

        var config = await flowService.GetConfigAsync(change, window.CheckingWindowType);

        journey.SelectedWhatToChange = change;
        journey.CheckingWindow = window;

        // A pupil search page refreshes the request state itself. An unknown flow leaves it alone.
        if (config is not null && !config.Pages.Any(p => p.Type == PageType.PupilSearch))
            ClearPreviousRequest(journey);

        await analytics.TrackSafeAsync(new ChangeTypeSelectedEvent
        {
            WhatToChange = change.ToString(),
            CheckingWindowType = window.CheckingWindowType.ToString(),
        });

        return new JourneyStart(true, config?.FirstPageId);
    }

    // Clear every per-request field of RequestState here. See docs/request-journey.md, Stage 2.
    private static void ClearPreviousRequest(RequestState journey)
    {
        journey.ReferenceNumber = null;
        journey.SelectedPupil = null;
        journey.SelectedPupilId = null;
        journey.SelectedPupilLabel = null;
        journey.MatchedPupil = null;
        journey.MatchedPupilId = null;
        journey.MatchedPupilLabel = null;
        journey.SelectedResult = null;
        journey.SelectedResultQualification = null;
        journey.SelectedQualification = null;
        journey.OriginCountryCode = null;
        journey.OriginCountryLanguages = null;
        journey.QuestionAnswers = new();
        journey.QuestionHistory = new();
    }
}
