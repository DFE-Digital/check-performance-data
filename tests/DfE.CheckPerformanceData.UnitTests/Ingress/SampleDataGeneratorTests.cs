using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

// #939-#941: the rules that every sample data generator and every shipped schema keep. The
// integration test SampleDataSchemaTests checks that the rows pass each schema.
public sealed class SampleDataGeneratorTests
{
    private static readonly string IngressFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Ingress");

    public static TheoryData<string> Generators()
    {
        var data = new TheoryData<string>();
        foreach (var generator in SampleDataGeneratorCatalogue.All)
            data.Add(generator.Name);
        return data;
    }

    private static readonly SampleDataGenerators Registry = new(SampleDataGeneratorCatalogue.All);

    private static IEnumerable<(string File, JObject Schema)> Schemas() =>
        Directory.EnumerateFiles(IngressFolder, "*.json", SearchOption.AllDirectories)
            .Select(file => (Path.GetRelativePath(IngressFolder, file), JObject.Parse(File.ReadAllText(file))));

    // The schema of the first file that names the generator, so a generator that reads its schema
    // makes real rows.
    private static SampleSchemaShape ShapeFor(string generator)
    {
        var file = Directory.EnumerateFiles(IngressFolder, "*.json", SearchOption.AllDirectories).Order()
            .First(f => JObject.Parse(File.ReadAllText(f))["x-ingress"]?["sample"]?.Value<string>() == generator);
        return SampleSchemaShape.From(JSchema.Parse(File.ReadAllText(file)));
    }

    [Fact]
    public void Each_generator_is_named_by_a_schema()
    {
        var named = Schemas().Select(s => s.Schema["x-ingress"]?["sample"]?.Value<string>()).ToHashSet();

        Assert.All(SampleDataGeneratorCatalogue.All, g => Assert.Contains(g.Name, named));
    }

    [Fact]
    public void Each_schema_names_a_generator_that_exists()
    {
        var unknown = Schemas()
            .Where(s => s.Schema["x-ingress"]?["sample"]?.Value<string>() is { } name && Registry.Find(name) is null)
            .Select(s => s.File)
            .ToList();

        Assert.Empty(unknown);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void A_generator_gives_the_same_rows_to_the_same_school(string name)
    {
        var generator = Registry.Find(name)!;
        var school = new SampleSchool("9334290", "123456");

        var first = generator.Rows(school, ShapeFor(name)).Select(r => r.OrderBy(kv => kv.Key).ToList()).ToList();
        var second = generator.Rows(school, ShapeFor(name)).Select(r => r.OrderBy(kv => kv.Key).ToList()).ToList();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(Generators))]
    public void A_generator_writes_the_schools_urn_and_never_another_schools_laestab(string name)
    {
        var rows = Registry.Find(name)!.Rows(new SampleSchool("9334290", "123456"), ShapeFor(name)).ToList();

        Assert.All(rows, row =>
        {
            if (row.TryGetValue("URN", out var urn)) Assert.Equal("123456", urn);
            if (row.TryGetValue("LAESTAB", out var laestab)) Assert.Equal("9334290", laestab);
        });
    }

    [Fact]
    public void Two_generators_cannot_have_one_name()
    {
        var generator = SampleDataGeneratorCatalogue.All[0];

        Assert.Throws<InvalidOperationException>(() => new SampleDataGenerators([generator, generator]));
    }
}
