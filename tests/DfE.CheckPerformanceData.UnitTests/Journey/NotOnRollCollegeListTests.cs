using System.Text.Json;
using DfE.CheckPerformanceData.Application.Journey.NotOnRoll;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#304119: the FE colleges that may choose "Not on roll", matched on LAESTAB.
public class NotOnRollCollegeListTests
{
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string BundledJson() =>
        File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web", "Data", "NotOnRollColleges", NotOnRollCollegeList.BlobName));

    [Theory]
    [InlineData("2118066")]
    [InlineData("211/8066")]
    [InlineData(" 211/8066 ")]
    public void Contains_matches_a_listed_laestab_in_any_format(string laestab)
    {
        var list = NotOnRollCollegeList.Parse("""{ "colleges": [ { "laestab": "2118066" } ] }""");

        Assert.True(list.Contains(laestab));
    }

    [Theory]
    [InlineData("2118067")]
    [InlineData("")]
    [InlineData(null)]
    public void Contains_does_not_match_an_unlisted_or_empty_laestab(string? laestab)
    {
        var list = NotOnRollCollegeList.Parse("""{ "colleges": [ { "laestab": "2118066" } ] }""");

        Assert.False(list.Contains(laestab));
    }

    [Fact]
    public void An_entry_with_no_laestab_is_ignored()
    {
        // A missing LAESTAB must not become an empty key that some empty claim could match.
        var list = NotOnRollCollegeList.Parse("""{ "colleges": [ { "urn": "130418" }, { "laestab": "" } ] }""");

        Assert.Equal(0, list.Count);
        Assert.False(list.Contains(""));
    }

    [Fact]
    public void Malformed_json_throws()
    {
        Assert.ThrowsAny<JsonException>(() => NotOnRollCollegeList.Parse("{ not json"));
    }

    [Fact]
    public void Empty_matches_nothing()
    {
        Assert.False(NotOnRollCollegeList.Empty.Contains("2118066"));
    }

    [Theory]
    [InlineData("2118066")] // New City College
    [InlineData("3138001")] // West Thames College
    [InlineData("3208601")] // Sir George Monoux College
    [InlineData("3308006")] // South and City College Birmingham
    [InlineData("3708001")] // Barnsley College
    [InlineData("8108001")] // Hull College
    [InlineData("8128005")] // TEC Partnership
    [InlineData("8808003")] // South Devon College
    [InlineData("8408002")] // Bishop Auckland College
    [InlineData("8868018")] // EKC Group
    [InlineData("9368001")] // North East Surrey College of Technology
    [InlineData("8408008")] // East Durham College
    [InlineData("3838025")] // Luminate Education Group
    [InlineData("8038000")] // South Gloucestershire and Stroud College
    public void The_bundled_file_lists_every_college_in_the_ticket(string laestab)
    {
        Assert.True(NotOnRollCollegeList.Parse(BundledJson()).Contains(laestab));
    }

    [Fact]
    public void The_bundled_file_lists_only_the_ticket_colleges()
    {
        Assert.Equal(14, NotOnRollCollegeList.Parse(BundledJson()).Count);
    }
}
