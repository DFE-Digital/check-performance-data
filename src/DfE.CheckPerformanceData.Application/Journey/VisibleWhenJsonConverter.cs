using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Reads <c>visibleWhen</c>, <c>optionalWhen</c> and <c>requireAtLeastOneWhen</c>. The value is
/// one entry or an array of entries. An entry is one of these:
/// <list type="bullet">
/// <item>a fact test: <c>{ "fact": "&lt;name&gt;", "is": true|false }</c>;</item>
/// <item>an answer test: <c>{ "answer": "&lt;question id&gt;", "in": ["&lt;value&gt;", ...] }</c>;</item>
/// <item>an any-of test: <c>{ "anyOf": [ &lt;entry&gt;, ... ] }</c>.</item>
/// </list>
/// A string is not an entry: a fact test always names its <c>"is"</c> value. Always writes the
/// array form.
/// </summary>
public sealed class VisibleWhenJsonConverter : JsonConverter<IReadOnlyList<JourneyConditionRef>>
{
    private const string Shape =
        "A condition object is { \"fact\": ..., \"is\": true|false }, { \"answer\": ..., \"in\": [...] } or { \"anyOf\": [...] }.";

    public override IReadOnlyList<JourneyConditionRef>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            return [ReadEntry(ref reader)];

        return ReadEntries(ref reader);
    }

    private static List<JourneyConditionRef> ReadEntries(ref Utf8JsonReader reader)
    {
        var entries = new List<JourneyConditionRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            entries.Add(ReadEntry(ref reader));
        return entries;
    }

    private static JourneyConditionRef ReadEntry(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
            throw new JsonException(
                $"A condition cannot be the string \"{reader.GetString()}\". Write {{ \"fact\": \"{reader.GetString()}\", \"is\": true }}.");

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"A condition is an object. {Shape}");

        string? fact = null;
        bool? @is = null;
        string? answer = null;
        List<string>? values = null;
        List<JourneyConditionRef>? anyOf = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            switch (property)
            {
                case "fact" when reader.TokenType == JsonTokenType.String:
                    fact = reader.GetString();
                    break;
                case "is" when reader.TokenType is JsonTokenType.True or JsonTokenType.False:
                    @is = reader.GetBoolean();
                    break;
                case "answer" when reader.TokenType == JsonTokenType.String:
                    answer = reader.GetString();
                    break;
                case "in" when reader.TokenType == JsonTokenType.StartArray:
                    values = [];
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.String)
                            throw new JsonException("Each value in a condition's \"in\" list is a string.");
                        values.Add(reader.GetString()!);
                    }
                    break;
                case "anyOf" when reader.TokenType == JsonTokenType.StartArray:
                    anyOf = ReadEntries(ref reader);
                    break;
                default:
                    throw new JsonException($"A condition object cannot have \"{property}\" here. {Shape}");
            }
        }

        if (fact is not null && @is is not null && answer is null && values is null && anyOf is null)
        {
            if (string.IsNullOrWhiteSpace(fact))
                throw new JsonException("A fact test needs a fact name.");
            return JourneyConditionRef.Fact(fact, @is.Value);
        }

        if (answer is not null && fact is null && @is is null && anyOf is null)
        {
            if (string.IsNullOrWhiteSpace(answer) || values is not { Count: > 0 })
                throw new JsonException("An answer test needs \"answer\" and a non-empty \"in\" list.");
            return new JourneyConditionRef { Answer = answer, In = values };
        }

        if (anyOf is not null && fact is null && @is is null && answer is null && values is null)
        {
            if (anyOf.Count < 2)
                throw new JsonException("An \"anyOf\" list needs two or more entries.");
            return new JourneyConditionRef { AnyOf = anyOf };
        }

        throw new JsonException(Shape);
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<JourneyConditionRef> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value) WriteEntry(writer, entry);
        writer.WriteEndArray();
    }

    private static void WriteEntry(Utf8JsonWriter writer, JourneyConditionRef entry)
    {
        if (entry.IsAnyOf)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("anyOf");
            foreach (var inner in entry.AnyOf!) WriteEntry(writer, inner);
            writer.WriteEndArray();
            writer.WriteEndObject();
            return;
        }

        if (entry.IsAnswerTest)
        {
            writer.WriteStartObject();
            writer.WriteString("answer", entry.Answer);
            writer.WriteStartArray("in");
            foreach (var v in entry.In ?? []) writer.WriteStringValue(v);
            writer.WriteEndArray();
            writer.WriteEndObject();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("fact", entry.Name);
        writer.WriteBoolean("is", entry.Is);
        writer.WriteEndObject();
    }
}
