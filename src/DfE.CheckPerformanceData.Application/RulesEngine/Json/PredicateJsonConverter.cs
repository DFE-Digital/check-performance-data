using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.RulesEngine.Json;

/// <summary>
/// Reads a JSON node into a <see cref="Predicate"/>.
///
/// Disambiguation is by shape, not by a tag field:
/// <list type="bullet">
///   <item>The string literal <c>"otherwise"</c> → <see cref="Predicate.Otherwise"/>.</item>
///   <item>An object with <c>all</c> → <see cref="Predicate.AllOf"/>.</item>
///   <item>An object with <c>any</c> → <see cref="Predicate.AnyOf"/>.</item>
///   <item>An object with <c>not</c> → <see cref="Predicate.Not"/>.</item>
///   <item>An object with <c>field</c> + (<c>eq</c>|<c>neq</c>|<c>in</c>|<c>lt</c>|<c>lte</c>|<c>gt</c>|<c>gte</c>)
///         → the corresponding field predicate.</item>
///   <item>An object with <c>isKnownAndCertain</c> → <see cref="Predicate.IsKnownAndCertain"/>.</item>
///   <item>An object with <c>officialLanguageIs</c> + <c>countryField</c> → <see cref="Predicate.OfficialLanguageIs"/>.</item>
/// </list>
/// Any other shape is a <see cref="JsonException"/>.
/// </summary>
public sealed class PredicateJsonConverter : JsonConverter<Predicate>
{
    private static readonly FieldValueJsonConverter FieldValueConverter = new();

    public override Predicate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Terminal "otherwise" literal
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.Equals(s, "otherwise", StringComparison.OrdinalIgnoreCase))
                return Predicate.Otherwise.Instance;
            throw new JsonException($"Unexpected string literal '{s}' for predicate. Expected 'otherwise'.");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected object or 'otherwise' string for predicate; got {reader.TokenType}.");

        // Buffer the whole object so we can look at keys in any order.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (TryRead(root, "all",  out var allList,  options) is { } all)  return new Predicate.AllOf(all);
        if (TryRead(root, "any",  out var anyList,  options) is { } any)  return new Predicate.AnyOf(any);
        if (root.TryGetProperty("not", out var notInner))
        {
            return new Predicate.Not(ReadPredicate(notInner, options));
        }
        if (root.TryGetProperty("isKnownAndCertain", out var iknown))
        {
            return new Predicate.IsKnownAndCertain(iknown.GetString()
                ?? throw new JsonException("isKnownAndCertain requires a non-null field name."));
        }
        if (root.TryGetProperty("officialLanguageIs", out var lang))
        {
            if (!root.TryGetProperty("countryField", out var c))
                throw new JsonException("officialLanguageIs requires a countryField.");
            var country = c.GetString()
                ?? throw new JsonException("officialLanguageIs requires a countryField.");
            var language = lang.GetString()
                ?? throw new JsonException("officialLanguageIs requires a language name.");
            return new Predicate.OfficialLanguageIs(country, language);
        }

        if (root.TryGetProperty("field", out var fieldEl))
        {
            var field = fieldEl.GetString()
                ?? throw new JsonException("'field' must be a non-null string.");
            if (root.TryGetProperty("eq",  out var eq))  return new Predicate.FieldEq(field,  ReadLiteral(eq, options));
            if (root.TryGetProperty("neq", out var neq)) return new Predicate.FieldNeq(field, ReadLiteral(neq, options));
            if (root.TryGetProperty("in",  out var inArr))
            {
                if (inArr.ValueKind != JsonValueKind.Array)
                    throw new JsonException("'in' must be an array.");
                var vals = new List<FieldValue>(inArr.GetArrayLength());
                foreach (var item in inArr.EnumerateArray()) vals.Add(ReadLiteral(item, options));
                return new Predicate.FieldIn(field, vals);
            }
            if (root.TryGetProperty("lt",  out var lt))  return new Predicate.FieldCompare(field, CompareOp.Lt,  ReadLiteral(lt,  options));
            if (root.TryGetProperty("lte", out var lte)) return new Predicate.FieldCompare(field, CompareOp.Lte, ReadLiteral(lte, options));
            if (root.TryGetProperty("gt",  out var gt))  return new Predicate.FieldCompare(field, CompareOp.Gt,  ReadLiteral(gt,  options));
            if (root.TryGetProperty("gte", out var gte)) return new Predicate.FieldCompare(field, CompareOp.Gte, ReadLiteral(gte, options));
            throw new JsonException($"Field predicate on '{field}' is missing an operator (eq/neq/in/lt/lte/gt/gte).");
        }

        throw new JsonException("Unrecognised predicate shape. Expected one of: all, any, not, field+op, isKnownAndCertain, officialLanguageIs, or the literal string 'otherwise'.");
    }

    public override void Write(Utf8JsonWriter writer, Predicate value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case Predicate.Otherwise: writer.WriteStringValue("otherwise"); return;
            case Predicate.AllOf all: WriteList(writer, "all", all.Items, options); return;
            case Predicate.AnyOf any: WriteList(writer, "any", any.Items, options); return;
            case Predicate.Not not:
                writer.WriteStartObject();
                writer.WritePropertyName("not");
                Write(writer, not.Inner, options);
                writer.WriteEndObject();
                return;
            case Predicate.IsKnownAndCertain iknown:
                writer.WriteStartObject();
                writer.WriteString("isKnownAndCertain", iknown.Field);
                writer.WriteEndObject();
                return;
            case Predicate.OfficialLanguageIs lang:
                writer.WriteStartObject();
                writer.WriteString("officialLanguageIs", lang.Language);
                writer.WriteString("countryField", lang.CountryField);
                writer.WriteEndObject();
                return;
            case Predicate.FieldEq eq:       WriteFieldOp(writer, eq.Field, "eq",  eq.Value, options); return;
            case Predicate.FieldNeq neq:     WriteFieldOp(writer, neq.Field, "neq", neq.Value, options); return;
            case Predicate.FieldIn inP:
                writer.WriteStartObject();
                writer.WriteString("field", inP.Field);
                writer.WritePropertyName("in");
                writer.WriteStartArray();
                foreach (var v in inP.Values) FieldValueConverter.Write(writer, v, options);
                writer.WriteEndArray();
                writer.WriteEndObject();
                return;
            case Predicate.FieldCompare cmp:
                WriteFieldOp(writer, cmp.Field, OpKey(cmp.Op), cmp.Value, options);
                return;
            default:
                throw new JsonException($"Cannot serialise {value.GetType().Name}");
        }
    }

    // --- helpers ---

    private static Predicate ReadPredicate(JsonElement el, JsonSerializerOptions options)
    {
        var json = el.GetRawText();
        return JsonSerializer.Deserialize<Predicate>(json, options)
            ?? throw new JsonException("Inner predicate deserialised to null.");
    }

    private static FieldValue ReadLiteral(JsonElement el, JsonSerializerOptions options)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => new FieldValue.Str(el.GetString()!),
            JsonValueKind.True   => new FieldValue.Bool(true),
            JsonValueKind.False  => new FieldValue.Bool(false),
            JsonValueKind.Number => new FieldValue.Num(el.GetDecimal()),
            JsonValueKind.Null   => FieldValue.Unknown.Instance,
            _ => throw new JsonException($"Unsupported literal value kind {el.ValueKind}.")
        };
    }

    /// <summary>Returns the children if the named array property exists; otherwise <c>null</c>.</summary>
    private static IReadOnlyList<Predicate>? TryRead(JsonElement root, string name,
        out IReadOnlyList<Predicate>? bag, JsonSerializerOptions options)
    {
        bag = null;
        if (!root.TryGetProperty(name, out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array)
            throw new JsonException($"'{name}' must be an array of predicates.");
        var list = new List<Predicate>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray()) list.Add(ReadPredicate(item, options));
        bag = list;
        return list;
    }

    private static void WriteList(Utf8JsonWriter writer, string name,
        IReadOnlyList<Predicate> items, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var item in items)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteFieldOp(Utf8JsonWriter writer, string field, string op,
        FieldValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("field", field);
        writer.WritePropertyName(op);
        FieldValueConverter.Write(writer, value, options);
        writer.WriteEndObject();
    }

    private static string OpKey(CompareOp op) => op switch
    {
        CompareOp.Lt  => "lt",
        CompareOp.Lte => "lte",
        CompareOp.Gt  => "gt",
        CompareOp.Gte => "gte",
        _ => throw new JsonException($"Unknown compare op {op}.")
    };
}
