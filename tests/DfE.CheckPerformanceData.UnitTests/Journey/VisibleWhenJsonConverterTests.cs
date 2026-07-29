using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class VisibleWhenJsonConverterTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void BareString_DeserializesToSingleElementList()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": "SchoolIsIndependent" }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal(["SchoolIsIndependent"], option.VisibleWhen);
    }

    [Fact]
    public void Array_DeserializesToList()
    {
        var json = """{ "value": "a", "label": "A", "visibleWhen": ["SchoolIsIndependent", "PupilIsNotAddBack"] }""";

        var option = JsonSerializer.Deserialize<QuestionOption>(json, Options)!;

        Assert.Equal(["SchoolIsIndependent", "PupilIsNotAddBack"], option.VisibleWhen);
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
        var option = new QuestionOption { Value = "a", Label = "A", VisibleWhen = ["Flag"] };

        var json = JsonSerializer.Serialize(option, Options);

        Assert.Contains("[\"Flag\"]", json);
    }

    [Fact]
    public void Question_OptionalWhen_BindsBareString()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files","optionalWhen":"EalWouldBeAutoRejected"}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Equal(["EalWouldBeAutoRejected"], question.OptionalWhen);
    }

    [Fact]
    public void Question_OptionalWhen_BindsArray()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files","optionalWhen":["A","B"]}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Equal(["A", "B"], question.OptionalWhen);
    }

    [Fact]
    public void Question_OptionalWhen_AbsentIsNull()
    {
        var json = """{"id":"evidence","type":"FileUpload","title":"Upload files"}""";
        var question = JsonSerializer.Deserialize<Question>(json, Options)!;
        Assert.Null(question.OptionalWhen);
    }
}
