using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.Conditions;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class HasSyllabusCodesForSelectedQualificationConditionTests
{
    private readonly HasSyllabusCodesForSelectedQualificationCondition _sut = new();

    private static JourneyConditionContext Ctx(QualificationReference? qualification)
    {
        var journey = new RequestState { SelectedQualification = qualification };
        return new JourneyConditionContext { Journey = journey, User = new JourneyUserContext() };
    }

    private static QualificationReference Qualification(params string[] codes) => new()
    {
        Qan = "60146084",
        QualificationTitle = "AQA Level 1/Level 2 GCSE (9-1) in Mathematics",
        AwardingOrganisation = "AQA",
        SyllabusCodes = [.. codes.Select(c => new SyllabusCode { Code = c, Title = c })]
    };

    [Fact]
    public void Name_IsTheNameTheConfigReferences() =>
        Assert.Equal("qualification.hasSyllabusCodes", _sut.Name);

    [Fact]
    public void WithSyllabusCodes_IsTrue() =>
        Assert.True(_sut.Evaluate(Ctx(Qualification("8300H", "8300F"))));

    [Fact]
    public void WithNoSyllabusCodes_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(Qualification())));

    [Fact]
    public void WithNoSelectedQualification_IsFalse() =>
        Assert.False(_sut.Evaluate(Ctx(null)));
}
