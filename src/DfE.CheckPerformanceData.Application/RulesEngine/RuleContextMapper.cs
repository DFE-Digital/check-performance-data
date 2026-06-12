using System.Globalization;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// Default <see cref="IRuleContextMapper"/>. Projects a queue
/// <see cref="RequestDocument"/> into a typed <see cref="RuleContext"/>:
///
/// <list type="bullet">
///   <item><c>OutcomeKey</c> from <see cref="RequestDocument.RequestTypeCode"/> via
///         <see cref="AnswerFieldMap.WhatToChangeToOutcomeKey"/>.</item>
///   <item><c>CheckingWindowType</c> from <see cref="RequestDocument.CheckingWindowType"/> via
///         <see cref="AnswerFieldMap.NormaliseCheckingWindowType"/>.</item>
///   <item><c>pupilAge</c> / <c>inclusionFlag</c> / <c>isAddBack</c> from the pupil
///         record on the message.</item>
///   <item><c>Fields</c> from <c>AnswerRecord[]</c> via the <see cref="AnswerFieldMap"/>
///         maps (plain copy, radio fan-out, vocabulary translation, window-type-resolved
///         sat-exams), parsed against the <see cref="FieldCatalogue"/>'s expected types.
///         The engine-facing <c>RawValue</c> is preferred over the display <c>Value</c>.</item>
/// </list>
///
/// Throws <see cref="RuleContextMappingException"/> when an answer's value is
/// present but cannot be parsed (e.g. <c>"not-a-date"</c> for a date-typed field).
/// The worker treats that as a synthetic Scrutiny per the fallback policy.
/// </summary>
public sealed class RuleContextMapper : IRuleContextMapper
{
    public RuleContext Map(RequestDocument message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var outcomeKey = ResolveOutcomeKey(message.RequestTypeCode);
        var checkingWindowType = AnswerFieldMap.NormaliseCheckingWindowType(message.CheckingWindowType);

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
        {
            ["checkingWindowType"] = string.IsNullOrEmpty(checkingWindowType) ? FieldValue.Unknown.Instance : new FieldValue.Str(checkingWindowType),
            ["requestType"]        = string.IsNullOrWhiteSpace(message.RequestTypeCode) ? FieldValue.Unknown.Instance : new FieldValue.Str(message.RequestTypeCode),
        };

        // Pupil-record fields. Age and Pincl are primitive ints; treat 0 / negative as
        // "not supplied" so the engine defers to Scrutiny rather than reading 0.
        if (message.Pupil is { } pupil)
        {
            if (pupil.Age > 0)
                fields["pupilAge"] = new FieldValue.Num(pupil.Age);
            if (pupil.Pincl > 0)
            {
                fields["inclusionFlag"] = new FieldValue.Str(pupil.Pincl.ToString(CultureInfo.InvariantCulture));
                fields["isAddBack"]     = new FieldValue.Bool(pupil.Pincl == AnswerFieldMap.AddBackPincl);
            }
        }

        if (message.Answers is not null)
        {
            foreach (var answer in message.Answers)
            {
                if (answer is null) continue;
                if (string.IsNullOrEmpty(answer.QuestionId)) continue;
                MapAnswer(answer, checkingWindowType, fields);
            }
        }

        return new RuleContext(outcomeKey, checkingWindowType, fields);
    }

    private static void MapAnswer(AnswerRecord answer, string checkingWindowType, Dictionary<string, FieldValue> fields)
    {
        var raw = (answer.RawValue ?? answer.Value)?.Trim();

        // Single-choice radio modelled as independent booleans by the rules.
        if (AnswerFieldMap.RadioFanOut.TryGetValue(answer.QuestionId, out var fanOut))
        {
            foreach (var (field, trigger) in fanOut)
            {
                fields[field] = string.IsNullOrEmpty(raw)
                    ? FieldValue.Unknown.Instance
                    : new FieldValue.Bool(string.Equals(raw, trigger, StringComparison.Ordinal));
            }
            return;
        }

        // One journey question, two canonical fields — resolved by checking window type.
        if (answer.QuestionId == AnswerFieldMap.SatExamsQuestionId)
        {
            var satField = AnswerFieldMap.SatExamsFieldFor(checkingWindowType);
            if (satField is null) return;
            fields[satField] = string.IsNullOrEmpty(raw)
                ? FieldValue.Unknown.Instance
                : ParseValue(satField, FieldType.Bool, raw);
            return;
        }

        // Journey vocabulary → canonical vocabulary (unlisted values fail safe to Unknown).
        if (AnswerFieldMap.TranslatedQuestions.TryGetValue(answer.QuestionId, out var translated))
        {
            fields[translated.Field] = !string.IsNullOrEmpty(raw) && translated.Values.TryGetValue(raw, out var value)
                ? value
                : FieldValue.Unknown.Instance;
            return;
        }

        // Plain copy, parsed by the catalogue's expected type.
        if (!AnswerFieldMap.QuestionToField.TryGetValue(answer.QuestionId, out var fieldName)) return;
        if (!FieldCatalogue.TryGetType(fieldName, out var expectedType))
        {
            // Catalogue and map are out of sync — defensive only; the validator
            // and the AnswerFieldMap tests should catch this in CI.
            return;
        }

        fields[fieldName] = string.IsNullOrEmpty(raw)
            ? FieldValue.Unknown.Instance
            : ParseValue(fieldName, expectedType, raw);
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
