using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class EstablishmentAmendmentRequestsViewSourceTests
{
    [Fact]
    public void Index_renders_status_presentation_from_the_view_model()
    {
        string source = ViewSource("Index.cshtml");

        Assert.Contains("row.TagClass", source);
        Assert.Contains("row.TagLabel", source);
        Assert.DoesNotContain("row.Status.ToString()", source);
    }

    [Fact]
    public void Details_back_link_returns_to_the_establishment_summary()
    {
        string source = ViewSource("UrnAmendmentView.cshtml");

        Assert.Contains("Url.Action(\"Index\", \"EstablishmentAmendmentRequests\")", source);
        Assert.DoesNotContain("Url.Action(\"Index\", \"AmendmentRequests\"", source);
    }

    private static string ViewSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepoRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views", "AmendmentRequests",
            "EstablishmentAmendmentRequests", fileName));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
