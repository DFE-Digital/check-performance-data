using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class QualificationReferenceBlobNameTests
{
    [Theory]
    [InlineData(QualificationReferenceSet.Post16, "qualification-reference.json")]
    [InlineData(QualificationReferenceSet.Ks4, "qualification-reference-ks4.json")]
    public void Each_reference_set_has_its_blob_name(QualificationReferenceSet set, string expected)
    {
        Assert.Equal(expected, ResultsEnquiryBlobPaths.QualificationReferenceBlobNameFor(set));
    }

    [Fact]
    public void No_two_reference_sets_share_a_blob_name()
    {
        var names = Enum.GetValues<QualificationReferenceSet>()
            .Select(ResultsEnquiryBlobPaths.QualificationReferenceBlobNameFor).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void A_value_that_is_not_a_reference_set_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResultsEnquiryBlobPaths.QualificationReferenceBlobNameFor((QualificationReferenceSet)99));
    }

    [Fact]
    public void Each_reference_set_has_a_bundled_seed_file()
    {
        foreach (var set in Enum.GetValues<QualificationReferenceSet>())
            Assert.True(File.Exists(SeedPath(ResultsEnquiryBlobPaths.QualificationReferenceBlobNameFor(set))), set.ToString());
    }

    private static string SeedPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var folder = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.Web", "Data", "QualificationReference");
            if (Directory.Exists(folder)) return Path.Combine(folder, fileName);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate Data/QualificationReference from " + AppContext.BaseDirectory);
    }
}
