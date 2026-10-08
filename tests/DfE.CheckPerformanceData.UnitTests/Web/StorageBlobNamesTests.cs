using DfE.CheckPerformanceData.Web.Admin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// One validator for every name the storage browser receives from a request: blob names on
// preview/download/delete, the folder prefix, the optional sub-folder, and the uploaded file's
// name. A name that fails here can never be a real blob, so the controller answers 404.
public sealed class StorageBlobNamesTests
{
    [Theory]
    [InlineData("a.csv")]
    [InlineData("folder/a.csv")]
    [InlineData("folder/sub/a.csv")]
    [InlineData("folder/.hidden")]
    [InlineData("a..b/c.txt")]
    [InlineData("report 2026 (final).csv")]
    public void IsValidBlobName_AcceptsOrdinaryNames(string name) =>
        Assert.True(StorageBlobNames.IsValidBlobName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../a.csv")]
    [InlineData("a/../b.csv")]
    [InlineData("./a.csv")]
    [InlineData("a/./b.csv")]
    [InlineData("/a.csv")]
    [InlineData("a//b.csv")]
    [InlineData("a\\b.csv")]
    [InlineData("a\u0000b.csv")]
    [InlineData("a\nb.csv")]
    [InlineData("a\u007fb.csv")]
    [InlineData("folder/")]
    public void IsValidBlobName_RejectsDotSegmentsControlCharactersAndSeparators(string? name) =>
        Assert.False(StorageBlobNames.IsValidBlobName(name));

    [Fact]
    public void IsValidBlobName_RejectsNamesOverTheAzureLimit()
    {
        Assert.True(StorageBlobNames.IsValidBlobName(new string('a', StorageBlobNames.MaxLength)));
        Assert.False(StorageBlobNames.IsValidBlobName(new string('a', StorageBlobNames.MaxLength + 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("folder")]
    [InlineData("folder/")]
    [InlineData("folder/sub/")]
    public void IsValidPrefix_AcceptsEmptyAndFolderPrefixes(string? prefix) =>
        Assert.True(StorageBlobNames.IsValidPrefix(prefix));

    [Theory]
    [InlineData("../")]
    [InlineData("a/../")]
    [InlineData("/a/")]
    [InlineData("a//b/")]
    [InlineData("a\\b/")]
    [InlineData("./")]
    public void IsValidPrefix_RejectsDotSegmentsAndSeparators(string prefix) =>
        Assert.False(StorageBlobNames.IsValidPrefix(prefix));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("foo", "foo/")]
    [InlineData("foo/", "foo/")]
    [InlineData("/foo/bar/", "foo/bar/")]
    public void NormalizePrefix_IsEmptyOrEndsInExactlyOneSlash(string? prefix, string expected) =>
        Assert.Equal(expected, StorageBlobNames.NormalizePrefix(prefix));

    [Theory]
    [InlineData(null, null, "a.csv", "a.csv")]
    [InlineData("reports/", null, "a.csv", "reports/a.csv")]
    [InlineData("reports/", "2026", "a.csv", "reports/2026/a.csv")]
    [InlineData("reports/", "/2026/", "a.csv", "reports/2026/a.csv")]
    [InlineData(null, "x/y", "a.csv", "x/y/a.csv")]
    [InlineData(null, null, "C:\\Users\\me\\a.csv", "a.csv")]
    [InlineData(null, null, "/tmp/a.csv", "a.csv")]
    public void ResolveUploadName_JoinsPrefixFolderAndLeafFileName(string? prefix, string? folder, string fileName, string expected) =>
        Assert.Equal(expected, StorageBlobNames.ResolveUploadName(prefix, folder, fileName));

    [Theory]
    [InlineData("../", null, "a.csv")]
    [InlineData("a/../", null, "a.csv")]
    [InlineData(null, "..", "a.csv")]
    [InlineData(null, "x/../y", "a.csv")]
    [InlineData(null, null, "")]
    [InlineData(null, null, null)]
    [InlineData(null, null, "a\u0000.csv")]
    public void ResolveUploadName_IsNullWhenAnyPartIsInvalid(string? prefix, string? folder, string? fileName) =>
        Assert.Null(StorageBlobNames.ResolveUploadName(prefix, folder, fileName));
}
