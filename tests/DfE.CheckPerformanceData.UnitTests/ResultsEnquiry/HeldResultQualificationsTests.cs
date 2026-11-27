using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class HeldResultQualificationsTests
{
    private const string Ks4Json = """
        {
          "60147891": { "qan": "60147891", "qualificationTitle": "From the KS4 list", "awardingOrganisation": "Pearson", "grades": ["9"], "syllabusCodes": [] },
          "60146084": { "qan": "60146084", "qualificationTitle": "From the KS4 list", "awardingOrganisation": "AQA", "grades": ["9"], "syllabusCodes": [] }
        }
        """;

    private const string Post16Json = """
        {
          "6016041X": { "qan": "6016041X", "qualificationTitle": "From the 16-19 list", "awardingOrganisation": "Active IQ", "grades": ["D"], "syllabusCodes": [] },
          "60146084": { "qan": "60146084", "qualificationTitle": "From the 16-19 list", "awardingOrganisation": "AQA", "grades": ["9"], "syllabusCodes": [] }
        }
        """;

    private readonly IQualificationReferenceClient _client = Substitute.For<IQualificationReferenceClient>();

    public HeldResultQualificationsTests()
    {
        _client.GetLookupAsync(QualificationReferenceSet.Ks4, Arg.Any<CancellationToken>())
            .Returns(QualificationReferenceLookup.Parse(Ks4Json));
        _client.GetLookupAsync(QualificationReferenceSet.Post16, Arg.Any<CancellationToken>())
            .Returns(QualificationReferenceLookup.Parse(Post16Json));
    }

    [Fact]
    public async Task A_ks4_autumn_result_that_only_the_ks4_list_holds_is_found_there()
    {
        var found = await _client.FindHeldResultAsync(CheckingWindowType.KS4Autumn, "60147891");

        Assert.Equal("From the KS4 list", found?.QualificationTitle);
        await _client.DidNotReceive().GetLookupAsync(QualificationReferenceSet.Post16, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ks4_autumn_result_that_only_the_16_19_list_holds_is_found_there()
    {
        var found = await _client.FindHeldResultAsync(CheckingWindowType.KS4Autumn, "6016041X");

        Assert.Equal("From the 16-19 list", found?.QualificationTitle);
    }

    [Fact]
    public async Task A_ks4_autumn_result_that_both_lists_hold_comes_from_the_ks4_list()
    {
        var found = await _client.FindHeldResultAsync(CheckingWindowType.KS4Autumn, "60146084");

        Assert.Equal("From the KS4 list", found?.QualificationTitle);
    }

    [Fact]
    public async Task A_16_19_result_is_never_looked_up_in_the_ks4_list()
    {
        var found = await _client.FindHeldResultAsync(CheckingWindowType.Post16, "60147891");

        Assert.Null(found);
        await _client.DidNotReceive().GetLookupAsync(QualificationReferenceSet.Ks4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_16_19_result_that_both_lists_hold_comes_from_the_16_19_list()
    {
        var found = await _client.FindHeldResultAsync(CheckingWindowType.Post16, "60146084");

        Assert.Equal("From the 16-19 list", found?.QualificationTitle);
    }

    [Fact]
    public async Task A_result_that_no_list_holds_is_not_found()
    {
        Assert.Null(await _client.FindHeldResultAsync(CheckingWindowType.KS4Autumn, "00000000"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_blank_qan_is_not_found_and_reads_no_document(string? qan)
    {
        Assert.Null(await _client.FindHeldResultAsync(CheckingWindowType.KS4Autumn, qan));
        await _client.DidNotReceive().GetLookupAsync(Arg.Any<QualificationReferenceSet>(), Arg.Any<CancellationToken>());
    }
}
