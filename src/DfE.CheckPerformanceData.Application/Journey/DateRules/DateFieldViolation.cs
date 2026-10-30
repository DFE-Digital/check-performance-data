namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>A date rule failure, on the question that the user must correct.</summary>
/// <param name="QuestionId">The raw question id from the flow config. It becomes the ModelState
/// key, and <c>JourneyViewModelBuilder</c> finds the error by it.</param>
public sealed record DateFieldViolation(string QuestionId, string Message);
