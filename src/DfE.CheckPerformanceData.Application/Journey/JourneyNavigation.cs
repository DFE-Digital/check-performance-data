namespace DfE.CheckPerformanceData.Application.Journey;

public abstract record JourneyNavigation;

public sealed record RedirectToJourneyPage(string PageId) : JourneyNavigation;

public sealed record RedirectToJourneySummary : JourneyNavigation;
