using System.Globalization;
using DfE.CheckPerformanceData.Domain.QueueMessages;

namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Default <see cref="IRuleContextMapper"/>. Projects a queue
/// <see cref="RequestMessage"/> into a typed <see cref="RuleContext"/>:
///
/// <list type="bullet">
///   <item><c>OutcomeKey</c> from <see cref="RequestMessage.WhatToChange"/> via
///         <see cref="AnswerFieldMap.WhatToChangeToOutcomeKey"/>.</item>
///   <item><c>KeyStage</c> from <see cref="RequestMessage.CheckingWindowType"/> via
///         <see cref="AnswerFieldMap.NormaliseKeyStage"/>.</item>
///   <item><c>Fields</c> from <c>Answer[]</c> via
///         <see cref="AnswerFieldMap.QuestionToField"/>, parsed against the
///         <see cref="FieldCatalogue"/>'s expected types.</item>
/// </list>
///
/// Throws <see cref="RuleContextMappingException"/> when an answer's value is
/// present but cannot be parsed (e.g. <c>"not-a-date"</c> for a date-typed field).
/// The worker treats that as a synthetic Scrutiny per the fallback policy.
/// </summary>
public sealed class RuleContextMapper : IRuleContextMapper
{
    public RuleContext Map(RequestMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var outcomeKey = ResolveOutcomeKey(message.WhatToChange);
        var keyStage = AnswerFieldMap.NormaliseKeyStage(message.CheckingWindowType);

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["keyStage"]    = string.IsNullOrEmpty(keyStage) ? FieldValue.Unknown.Instance : new FieldValue.Str(keyStage),
            ["requestType"] = string.IsNullOrWhiteSpace(message.WhatToChange) ? FieldValue.Unknown.Instance : new FieldValue.Str(message.WhatToChange),
        };

        // Pupil.Age is a primitive int; treat 0 / negative as "not supplied" so the
        // engine defers to Scrutiny rather than reading a default value as 0.
        if (message.Pupil is { Age: > 0 } pupil)
        {
            fields["pupilAge"] = new FieldValue.Num(pupil.Age);
        }

        if (message.Answers is not null)
        {
            foreach (var answer in message.Answers)
            {
                if (answer is null) continue;
                if (string.IsNullOrEmpty(answer.QuestionId)) continue;
                if (!AnswerFieldMap.QuestionToField.TryGetValue(answer.QuestionId, out var fieldName)) continue;
                if (!FieldCatalogue.TryGetType(fieldName, out var expectedType))
                {
                    // Catalogue and map are out of sync — defensive only; the validator
                    // and the AnswerFieldMap tests should catch this in CI.
                    continue;
                }

                if (string.IsNullOrEmpty(answer.Value))
                {
                    fields[fieldName] = FieldValue.Unknown.Instance;
                    continue;
                }

                fields[fieldName] = ParseValue(fieldName, expectedType, answer.Value);
            }
        }

        return new RuleContext(outcomeKey, keyStage, fields);
    }

    private static string ResolveOutcomeKey(string? whatToChange)
    {
        if (string.IsNullOrWhiteSpace(whatToChange)) return AnswerFieldMap.UnknownOutcomeKey;
        return AnswerFieldMap.WhatToChangeToOutcomeKey.TryGetValue(whatToChange.Trim(), out var key)
            ? key
            : AnswerFieldMap.UnknownOutcomeKey;
    }

    private static FieldValue ParseValue(string fieldName, FieldType type, string raw)
    {
        var trimmed = raw.Trim();
        switch (type)
        {
            case FieldType.String:
                return new FieldValue.Str(trimmed);

            case FieldType.Bool:
                if (TryParseBool(trimmed, out var b)) return new FieldValue.Bool(b);
                throw new RuleContextMappingException(
                    $"Field '{fieldName}': cannot parse '{raw}' as Bool (expected true/false, yes/no, 1/0).");

            case FieldType.Number:
                if (decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    return new FieldValue.Num(d);
                throw new RuleContextMappingException(
                    $"Field '{fieldName}': cannot parse '{raw}' as Number.");

            case FieldType.Date:
                if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return new FieldValue.Date(dt);
                // Tolerate ISO-8601 with a time component (`2025-01-16T00:00:00Z`) by truncating.
                if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt2))
                    return new FieldValue.Date(DateOnly.FromDateTime(dt2));
                throw new RuleContextMappingException(
                    $"Field '{fieldName}': cannot parse '{raw}' as Date (expected yyyy-MM-dd).");

            default:
                throw new RuleContextMappingException($"Field '{fieldName}': unsupported field type {type}.");
        }
    }

    private static bool TryParseBool(string raw, out bool result)
    {
        switch (raw.ToLowerInvariant())
        {
            case "true":  case "yes": case "y": case "1": result = true;  return true;
            case "false": case "no":  case "n": case "0": result = false; return true;
            default:                                       result = false; return false;
        }
    }
}

/// <summary>
/// Thrown by <see cref="RuleContextMapper"/> when an answer carries a value
/// that cannot be parsed into the catalogue's expected type. Caught by the
/// worker and converted into a synthetic <see cref="DecisionStatus.Scrutiny"/>.
/// </summary>
public sealed class RuleContextMappingException : Exception
{
    public RuleContextMappingException(string message) : base(message) { }
    public RuleContextMappingException(string message, Exception inner) : base(message, inner) { }
}
