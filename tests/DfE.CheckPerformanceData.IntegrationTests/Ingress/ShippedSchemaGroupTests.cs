using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.IntegrationTests.Ingress;

// A schema that declares x-display.groups and breaks a group rule is shown ungrouped. That keeps
// every field on the page, but it loses the headings with no error. These tests read every shipped
// schema, so a group typo fails here. They check the rules, not which groups a schema has.
public sealed class ShippedSchemaGroupTests
{
    private static readonly string IngressFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Ingress");

    public static TheoryData<string> GroupedSchemas()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(IngressFolder, "*_schema.json", SearchOption.AllDirectories))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("x-display", out var display) &&
                display.ValueKind == JsonValueKind.Object && display.TryGetProperty("groups", out _))
                data.Add(Path.GetRelativePath(IngressFolder, file));
        }
        return data;
    }

    [Fact]
    public void At_least_one_shipped_schema_declares_groups()
    {
        Assert.NotEmpty(GroupedSchemas());
    }

    [Theory]
    [MemberData(nameof(GroupedSchemas))]
    public void A_grouped_schema_is_shown_grouped(string file)
    {
        var dataset = Parse(file);

        Assert.NotEmpty(dataset.Groups);
    }

    [Theory]
    [MemberData(nameof(GroupedSchemas))]
    public void Each_declared_group_has_a_visible_field(string file)
    {
        var dataset = Parse(file);

        Assert.All(dataset.Groups, group => Assert.Contains(dataset.Columns, c => c.Group == group.Key));
    }

    private static ExerciseDataset Parse(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(IngressFolder, file)));
        return new ExerciseDisplayService().ParseDefinition(file, null, json.RootElement);
    }
}
