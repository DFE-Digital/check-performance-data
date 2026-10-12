using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// The content the service ships with, read from the web project as it sits in the repository.
internal static class ShippedContent
{
    public static string WebProject { get; } = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", "src", "DfE.CheckPerformanceData.Web"));

    public static string ImportFolder => ManifestContentImporter.FolderIn(WebProject);

    public static ContentBundle Load(string file) =>
        ContentStagingJson.Deserialize(File.ReadAllText(Path.Combine(ImportFolder, file)))!;

    public static ContentBundle Fixtures() => Load(TestFixtureSeedBundle.FileName);

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
