using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Decides which pupil-data change types a window type offers, and starts the journey for the
/// change type that the user (or the service) selects on "What pupil data would you like to change?".
/// </summary>
public interface IChangeTypeSelectionService
{
    /// <summary>
    /// Gets the pupil-data change types that <paramref name="checkingWindowType"/> offers. A change
    /// type is offered only when its flow file exists. When exactly one is offered,
    /// <see cref="ChangeTypeChoice.OnlyChange"/> names it and the page is not shown.
    /// </summary>
    /// <param name="checkingWindowType">The window type of the checking window.</param>
    /// <returns>The offered change types, in display order. Never empty.</returns>
    /// <exception cref="InvalidOperationException">The window type offers no change type. This is a configuration error.</exception>
    Task<ChangeTypeChoice> GetChoiceAsync(CheckingWindowType checkingWindowType);

    /// <summary>
    /// Starts the journey for <paramref name="change"/> on <paramref name="journey"/>. When the
    /// window type offers the change type, it records the change type and the window, clears the
    /// previous request's state when the flow has no pupil search page, and sends
    /// <c>ChangeTypeSelectedEvent</c>. An analytics failure does not stop it. When the window type
    /// does not offer the change type, it changes nothing and sends no event.
    /// </summary>
    /// <param name="journey">The session state to update. The caller saves it when the start is recorded.</param>
    /// <param name="window">The checking window of the journey.</param>
    /// <param name="change">The selected change type.</param>
    /// <returns>
    /// <see cref="JourneyStart.Refused"/> when the window type does not offer the change type.
    /// Otherwise a recorded start. Its first page id is null when the change type has no flow config.
    /// It throws no exception of its own.
    /// </returns>
    Task<JourneyStart> StartJourneyAsync(RequestState journey, CheckingWindowDto window, WhatToChange change);
}
