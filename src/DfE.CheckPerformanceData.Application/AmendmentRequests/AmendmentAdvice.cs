using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentAdvice
{
    public required WhatToChange RequestType { get; init; }
    public required string PupilName { get; init; }
    public required string AdviceText { get; init; }
    public IReadOnlyList<string> EvidenceMessages { get; init; } = [];

    /// <summary>
    /// The selected removal reason's display label (e.g. "Social care involvement -
    /// including police or prison"). Populated only for Remove requests whose reason
    /// has been answered; <c>null</c> otherwise.
    /// </summary>
    public string? ReasonForRemoval { get; init; }

    public required AmendmentContinueTarget ContinueTarget { get; init; }
}
