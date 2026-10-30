namespace DfE.CheckPerformanceData.Application.Journey.DateRules;

/// <summary>
/// One entry in a <c>Date</c> question's <c>dateRules</c> list. An <see cref="DateRuleCheck.OnOrBefore"/>
/// or <see cref="DateRuleCheck.OnOrAfter"/> rule names exactly one target: <see cref="Question"/>,
/// <see cref="Fact"/> or <see cref="Date"/>. <c>DateRuleConfigTests</c> checks this for each flow.
/// </summary>
public sealed record DateRule
{
    public DateRuleCheck Check { get; init; }

    /// <summary>The id of another <c>Date</c> question in the same flow.</summary>
    public string? Question { get; init; }

    /// <summary>The name of an <see cref="IJourneyDateFact"/>, for example <c>cohort.start</c>.</summary>
    public string? Fact { get; init; }

    /// <summary>A fixed date, written <c>yyyy-MM-dd</c> in the JSON.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The error message. It can contain <c>{pupilName}</c> and <c>{date}</c> (the target date).</summary>
    public required string Message { get; init; }
}
