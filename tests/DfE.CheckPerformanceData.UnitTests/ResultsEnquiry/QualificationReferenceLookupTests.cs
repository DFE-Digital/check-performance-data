using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public class QualificationReferenceLookupTests
{
    private const string Sample = """
        {
          "60146084": { "qan": "60146084", "qualificationTitle": "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
                        "awardingOrganisation": "AQA", "grades": ["1","2","3"],
                        "syllabusCodes": [ { "code": "8300F", "title": "Mathematics Foundation Tier" },
                                           { "code": "8300H", "title": "Mathematics Higher Tier" } ] },
          "6016041X": { "qan": "6016041X", "qualificationTitle": "Active IQ Level 2 Diploma",
                        "awardingOrganisation": "Active IQ", "grades": ["D","M","P"], "syllabusCodes": [] }
        }
        """;

    [Fact]
    public void Find_is_case_insensitive_and_trimmed()
    {
        // QANs ending in a letter (6016041X) arrive with unreliable casing; a case-sensitive miss
        // would tell the user their qualification does not exist.
        var lookup = QualificationReferenceLookup.Parse(Sample);
        Assert.Equal("Active IQ", lookup.Find(" 6016041x ")!.AwardingOrganisation);
        Assert.Null(lookup.Find("00000000"));
        Assert.Null(lookup.Find(null));
    }

    [Fact]
    public void Awarding_organisations_are_distinct_and_sorted()
    {
        var lookup = QualificationReferenceLookup.Parse(Sample);
        Assert.Equal(new[] { "Active IQ", "AQA" }, lookup.AwardingOrganisations);
    }

    [Fact]
    public void ForAwardingOrganisation_returns_that_AOs_qualifications_sorted_by_title()
    {
        var lookup = QualificationReferenceLookup.Parse(Sample);
        var aqa = lookup.ForAwardingOrganisation("AQA");
        Assert.Single(aqa);
        Assert.Equal("60146084", aqa[0].Qan);
        Assert.Empty(lookup.ForAwardingOrganisation("Nobody"));
    }

    [Fact]
    public void Syllabus_codes_parse_with_their_titles_in_document_order()
    {
        // Sibling codes often differ only by specialism (six Art & Design codes share one QAN),
        // so the title is the only human-readable difference the dropdown can show.
        var q = QualificationReferenceLookup.Parse(Sample).Find("60146084")!;
        Assert.Equal(new[] { "8300F", "8300H" }, q.SyllabusCodes.Select(c => c.Code).ToArray());
        Assert.Equal("Mathematics Higher Tier", q.SyllabusCodes[1].Title);
        Assert.Empty(QualificationReferenceLookup.Parse(Sample).Find("6016041X")!.SyllabusCodes);
    }

    [Fact]
    public void ToGradeReference_offers_every_grade_as_a_pass_grade()
    {
        // The grade picker validates through GradeReference.Offers. The supplier export has no
        // pass/fail split, and the missing grade is the user's claim — so every grade is offered
        // and none is ranked. FailGrades must be empty or the picker would render a phantom group.
        var reference = QualificationReferenceLookup.Parse(Sample).Find("60146084")!.ToGradeReference();
        Assert.True(reference.Offers("2"));
        Assert.False(reference.Offers("9"));
        Assert.Empty(reference.FailGrades);
        Assert.Equal("AQA", reference.AwardingOrganisation);
    }

    [Fact]
    public void Empty_and_malformed_documents_behave_like_the_grade_reference()
    {
        Assert.Same(QualificationReferenceLookup.Empty, QualificationReferenceLookup.Parse("{}"));
        Assert.Same(QualificationReferenceLookup.Empty, QualificationReferenceLookup.Parse("null"));
        Assert.ThrowsAny<Exception>(() => QualificationReferenceLookup.Parse("not json"));
    }
}
