using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RulesEngine.Json;

/// <summary>
/// Reads a JSON literal (<c>"KS4"</c>, <c>true</c>, <c>402</c>, <c>"2025-01-16"</c>)
/// into a <see cref="FieldValue"/> primitive. The converter never produces
/// <see cref="FieldValue.Date"/> directly — JSON has no date type, so date
/// literals arrive as strings and are realised as <see cref="FieldValue.Date"/>
/// by the validator using the field catalogue.
///
/// A date literal may instead be a window-relative object (#800):
/// <c>{"windowRelative":"dayMonthOfWindowYear","month":4,"day":1}</c>,
/// <c>{"windowRelative":"firstSeptemberMinusYears","years":3}</c>, or
/// <c>{"windowRelative":"secondMondayOfMay"}</c>. These read into
/// <see cref="FieldValue.RelativeDate"/>; the month/day/year ranges are checked
/// at install time by the validator, not here.
///
/// Also handles writing literals back out (for diagnostics / round-tripping).
/// </summary>
public sealed class FieldValueJsonConverter : JsonConverter<FieldValue>
{
    public override FieldValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return new FieldValue.Str(reader.GetString()!);
            case JsonTokenType.True:
            case JsonTokenType.False:
                return new FieldValue.Bool(reader.GetBoolean());
            case JsonTokenType.Number:
                return new FieldValue.Num(reader.GetDecimal());
            case JsonTokenType.Null:
                return FieldValue.Unknown.Instance;
            case JsonTokenType.StartObject:
                return ReadObjectLiteral(ref reader);
            default:
                throw new JsonException(
                    $"Unsupported literal type {reader.TokenType}; expected string, bool, number, null, or a window-relative date object.");
        }
    }

    /// <summary>
    /// An object literal is a window-relative date (#800), or a malformed object.
    /// A local function, not a switch case: the pool-borrowed document must be
    /// disposed on every exit, so it needs its own <c>using</c> scope.
    /// </summary>
    private static FieldValue ReadObjectLiteral(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadRelativeDateSpec(doc.RootElement) is { } spec
            ? new FieldValue.RelativeDate(spec)
            : throw new JsonException(
                "Unsupported object literal; expected a window-relative date with a 'windowRelative' token of dayMonthOfWindowYear, firstSeptemberMinusYears, or secondMondayOfMay.");
    }

    public override void Write(Utf8JsonWriter writer, FieldValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case FieldValue.Str s:       writer.WriteStringValue(s.Value); break;
            case FieldValue.Bool b:      writer.WriteBooleanValue(b.Value); break;
            case FieldValue.Num n:       writer.WriteNumberValue(n.Value); break;
            case FieldValue.Date d:      writer.WriteStringValue(d.Value.ToString("yyyy-MM-dd")); break;
            case FieldValue.RelativeDate rd: WriteRelativeDateSpec(writer, rd.Spec); break;
            case FieldValue.Unknown:     writer.WriteNullValue(); break;
            case FieldValue.Uncertain u: Write(writer, u.Inner, options); break;
            default: throw new JsonException($"Cannot serialise {value.GetType().Name}");
        }
    }

    /// <summary>
    /// Parses a <c>{"windowRelative":"&lt;token&gt;", ...}</c> object into a
    /// <see cref="RelativeDateSpec"/>. Returns null for any object that is not a
    /// well-formed one: a missing or unknown token, a property of the wrong type,
    /// or an unexpected extra property. Ranges (month, day, years) are the
    /// validator's job.
    /// </summary>
    internal static RelativeDateSpec? ReadRelativeDateSpec(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty("windowRelative", out var tokenEl)
            || tokenEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return tokenEl.GetString()! switch
        {
            "dayMonthOfWindowYear" => TryGetInt(el, "month", out var month) && TryGetInt(el, "day", out var day)
                && CheckNoExtras(el, "windowRelative", "month", "day")
                    ? new RelativeDateSpec.DayMonthOfWindowYear(month, day)
                    : null,
            "firstSeptemberMinusYears" => TryGetInt(el, "years", out var years)
                && CheckNoExtras(el, "windowRelative", "years")
                    ? new RelativeDateSpec.FirstSeptemberMinusYears(years)
                    : null,
            "secondMondayOfMay" => CheckNoExtras(el, "windowRelative")
                ? new RelativeDateSpec.SecondMondayOfMay()
                : null,
            _ => null,
        };
    }

    private static void WriteRelativeDateSpec(Utf8JsonWriter writer, RelativeDateSpec spec)
    {
        writer.WriteStartObject();
        writer.WriteString("windowRelative", spec.WindowRelative);
        switch (spec)
        {
            case RelativeDateSpec.DayMonthOfWindowYear d:
                writer.WriteNumber("month", d.Month);
                writer.WriteNumber("day", d.Day);
                break;
            case RelativeDateSpec.FirstSeptemberMinusYears y:
                writer.WriteNumber("years", y.Years);
                break;
        }
        writer.WriteEndObject();
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        return el.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out value);
    }

    /// <summary>True iff every property of the object is one of <paramref name="expected"/>.</summary>
    private static bool CheckNoExtras(JsonElement el, params string[] expected)
    {
        foreach (var property in el.EnumerateObject())
        {
            if (!expected.Contains(property.Name)) return false;
        }
        return true;
    }
}
