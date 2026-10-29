namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// One entry in a <c>visibleWhen</c>, <c>optionalWhen</c> or <c>requireAtLeastOneWhen</c> list.
/// It is one of three forms:
/// <list type="bullet">
/// <item>a fact test: true when the <see cref="IJourneyCondition"/> named <see cref="Name"/>
/// gives <see cref="Is"/>. In JSON it is <c>{ "fact": "school.isIndependent", "is": false }</c>.</item>
/// <item>an answer test: true when the answer to question <see cref="Answer"/> is one of the
/// values in <see cref="In"/>. In JSON it is
/// <c>{ "answer": "first-language", "in": ["other", "not-known"] }</c>.</item>
/// <item>an any-of test: true when one or more entries in <see cref="AnyOf"/> are true. In JSON
/// it is <c>{ "anyOf": [ ... ] }</c>.</item>
/// </list>
/// </summary>
public sealed record JourneyConditionRef
{
    public string? Name { get; init; }

    /// <summary>The value that the fact must give. Used only by a fact test.</summary>
    public bool Is { get; init; }

    public string? Answer { get; init; }

    public IReadOnlyList<string>? In { get; init; }

    public IReadOnlyList<JourneyConditionRef>? AnyOf { get; init; }

    public bool IsAnswerTest => Answer is not null;

    public bool IsAnyOf => AnyOf is not null;

    public static JourneyConditionRef Fact(string name, bool @is) => new() { Name = name, Is = @is };

    public static JourneyConditionRef AnswerIn(string questionId, params string[] values) =>
        new() { Answer = questionId, In = values };

    public static JourneyConditionRef AnyOfThese(params JourneyConditionRef[] entries) =>
        new() { AnyOf = entries };

    public bool Equals(JourneyConditionRef? other) =>
        other is not null
        && Name == other.Name
        && Is == other.Is
        && Answer == other.Answer
        && (In ?? []).SequenceEqual(other.In ?? [])
        && IsAnyOf == other.IsAnyOf
        && (AnyOf ?? []).SequenceEqual(other.AnyOf ?? []);

    public override int GetHashCode() => HashCode.Combine(Name, Is, Answer, In?.Count, AnyOf?.Count);

    public override string ToString() =>
        IsAnyOf ? $"any of [{string.Join(", ", AnyOf!)}]"
        : IsAnswerTest ? $"{Answer} in [{string.Join(", ", In ?? [])}]"
        : $"{Name} is {(Is ? "true" : "false")}";
}
