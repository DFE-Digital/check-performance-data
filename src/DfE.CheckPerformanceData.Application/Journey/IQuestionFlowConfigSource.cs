using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Reads a journey's question flow config, keyed by
/// <c>{WhatToChange}_{CheckingWindowType}</c>. Returns null when no config exists for that pair.
///
/// Read-only on purpose. The configs ship inside the release image and are served from it, so
/// nothing writes one at runtime — the upload method this interface used to carry existed only
/// for the Development-gated blob seeding step that has been removed. See
/// <c>docs/question-flow-deployment.md</c>.
/// </summary>
public interface IQuestionFlowConfigSource
{
    Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType);
}
