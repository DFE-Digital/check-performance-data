using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class VisibleWhenJsonConverterTests
{
    // Mirrors FileSystemQuestionFlowClient's deserialization options.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static JourneyConditionRef Fact(string name) => JourneyConditionRef.Fact(name, true);

    [Theory]
    [InlineData("\"school.isIndependent\"")]
    [InlineData("""["school.isIndependent"]""")]
    [InlineData("""[{ "fact": "pupil.isAddBack", "is": false }, "school.isIndependent"]""")]
    public void String_IsNotACondition_AndThrows(string visibleWhen)
    {
        var json = $$"""{ "value": "a", "label": "A", "visibleWhen": {{visibleWhen}} }""";

        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QuestionOption>(json, Options));

        Assert.Contains("{ \"fact\": \"school.isIndependent\", \"is\": true }", ex.Message);
    }

    [Fact]
    public void SingleObject_DeserializesToSingleElementList()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": { "fact": "school.isIndependent", "is": true } }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal([Fact("school.isIndependent")], option.VisibleWhen);
    }

    [Fact]
    public void Array_DeserializesToList()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": [{ "fact": "school.isIndependent", "is": true }, { "fact": "pupil.isAddBack", "is": false }] }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal([Fact("school.isIndependent"), JourneyConditionRef.Fact("pupil.isAddBack", false)], option.VisibleWhen);
    }

    [Fact]
    public void Absent_DeserializesToNull()
    {
        var json = """{ "value": "a", "label": "A" }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Null(option.VisibleWhen);
    }

    [Fact]
    public void Serializes_AsArray()
    {
        var option = new QuestionOption { Value = "a", Label = "A", VisibleWhen = [Fact("Flag")] };

        var json = JsonSerializer.Serialize(option, Options);

        Assert.Contains("""[{"fact":"Flag","is":true}]""", json);
    }

    [Fact]
    public void Question_OptionalWhen_BindsSingleObject()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files","optionalWhen":{"fact":"school.isIndependent","is":true}}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Equal([Fact("school.isIndependent")], question.OptionalWhen);
    }

    [Fact]
    public void Question_OptionalWhen_BindsArray()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files","optionalWhen":[{"fact":"A","is":true},{"fact":"B","is":true}]}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Equal([Fact("A"), Fact("B")], question.OptionalWhen);
    }

    [Fact]
    public void Question_OptionalWhen_AbsentIsNull()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files"}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Null(question.OptionalWhen);
    }

    // ── Answer tests ─────────────────────────────────────────────────────────

    [Fact]
    public void AnswerTest_Object_DeserializesToAnswerTest()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"t","optionalWhen":{ "answer": "first-language", "in": ["other", "not-known"] }}""";

        var question = JsonSerializer.Deserialize<Question>(json, Options)!;

        Assert.Equal([JourneyConditionRef.AnswerIn("first-language", "other", "not-known")], question.OptionalWhen);
    }

    [Fact]
    public void Array_MixesFactAndAnswerTests_InOrder()
    {
        var json = """{"id":"p","title":"t","requireAtLeastOne":true,"questions":[],"requireAtLeastOneWhen":[{"fact":"school.isIndependent","is":true},{"answer":"reason","in":["other"]}]}""";

        var page = JsonSerializer.Deserialize<JourneyPage>(json, Options)!;

        Assert.Equal(
            [Fact("school.isIndependent"), JourneyConditionRef.AnswerIn("reason", "other")],
            page.RequireAtLeastOneWhen);
    }

    [Fact]
    public void AnswerTest_RoundTrips_AsAnObject()
    {
        var option = new QuestionOption
        {
            Value = "a", Label = "A",
            VisibleWhen = [Fact("Flag"), JourneyConditionRef.AnswerIn("reason", "x", "y")]
        };

        var json = JsonSerializer.Serialize(option, Options);
        var back = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Contains("""[{"fact":"Flag","is":true},{"answer":"reason","in":["x","y"]}]""", json);
        Assert.Equal(option.VisibleWhen, back.VisibleWhen);
    }

    [Fact]
    public void FactTest_ReadsAsAFactWithItsValue()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": [{ "fact": "pupil.isAddBack", "is": false }] }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal([JourneyConditionRef.Fact("pupil.isAddBack", false)], option.VisibleWhen);
    }

    [Fact]
    public void AnyOf_ReadsItsEntries()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": [{ "anyOf": [{ "fact": "school.isIndependent", "is": true }, { "answer": "reason", "in": ["other"] }] }] }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal(
            [JourneyConditionRef.AnyOfThese(Fact("school.isIndependent"), JourneyConditionRef.AnswerIn("reason", "other"))],
            option.VisibleWhen);
    }

    [Fact]
    public void FactTestAndAnyOf_RoundTrip()
    {
        var option = new QuestionOption
        {
            Value = "a", Label = "A",
            VisibleWhen =
            [
                JourneyConditionRef.Fact("pupil.isAddBack", false),
                JourneyConditionRef.AnyOfThese(Fact("A"), JourneyConditionRef.Fact("B", false)),
            ]
        };

        var json = JsonSerializer.Serialize(option, Options);
        var back = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Contains("""[{"fact":"pupil.isAddBack","is":false},{"anyOf":[{"fact":"A","is":true},{"fact":"B","is":false}]}]""", json);
        Assert.Equal(option.VisibleWhen, back.VisibleWhen);
    }

    [Theory]
    [InlineData("""{ "fact": "school.isIndependent" }""")]
    [InlineData("""{ "is": true }""")]
    [InlineData("""{ "fact": "", "is": true }""")]
    [InlineData("""{ "fact": "school.isIndependent", "is": "false" }""")]
    [InlineData("""{ "fact": "school.isIndependent", "is": true, "in": ["x"] }""")]
    [InlineData("""{ "anyOf": [] }""")]
    [InlineData("""{ "anyOf": [{ "fact": "A", "is": true }] }""")]
    [InlineData("""{ "anyOf": [{ "fact": "A", "is": true }, { "fact": "B", "is": true }], "fact": "C", "is": true }""")]
    [InlineData("""{ "answer": "reason", "in": ["other"], "is": true }""")]
    [InlineData("""{ "answer": "reason" }""")]
    [InlineData("""{ "in": ["other"] }""")]
    [InlineData("""{ "answer": "reason", "in": [] }""")]
    [InlineData("""{ "answer": "", "in": ["other"] }""")]
    [InlineData("""{ "answer": "reason", "in": [1] }""")]
    [InlineData("""{ "answer": "reason", "in": ["other"], "not": true }""")]
    [InlineData("""42""")]
    public void MalformedEntry_Throws(string entry)
    {
        var json = $$"""{ "value": "a", "label": "A", "visibleWhen": [{{entry}}] }""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QuestionOption>(json, Options));
    }
}
