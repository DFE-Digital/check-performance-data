using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class EnquirySubjectTests
{
    private static ResultRecord Result(string qan, string session, string source = ResultsFileTags.Post16Included) =>
        new() { CypmdId = "500002", Qan = qan, Session = session, Grade = "M", SourceFile = source };

    private static QualificationReference Qualification(string qan) =>
        new() { Qan = qan, QualificationTitle = "A made-up qualification" };

    [Fact]
    public void The_same_result_in_two_files_has_one_subject() =>
        Assert.Equal(
            EnquirySubject.ForResult(Result("60172186", "S2024")),
            EnquirySubject.ForResult(Result("60172186", "S2024", ResultsFileTags.Post16LateResults1)));

    [Fact]
    public void Padding_and_casing_do_not_make_two_subjects() =>
        Assert.Equal(
            EnquirySubject.ForResult(Result("6037116X", "S2024")),
            EnquirySubject.ForResult(Result(" 6037116x ", " s2024 ")));

    [Theory]
    [InlineData("60172186", "S2023")]
    [InlineData("60146084", "S2024")]
    public void Another_session_or_qualification_is_another_subject(string qan, string session) =>
        Assert.NotEqual(
            EnquirySubject.ForResult(Result("60172186", "S2024")),
            EnquirySubject.ForResult(Result(qan, session)));

    [Fact]
    public void A_held_result_has_a_session_and_a_missing_qualification_has_none()
    {
        Assert.True(EnquirySubject.ForResult(Result("60172186", "S2024")).IsHeldResult);
        Assert.Null(EnquirySubject.ForQualification(Qualification("60172186")).Session);
        Assert.False(EnquirySubject.ForQualification(Qualification("60172186")).IsHeldResult);
    }

    [Fact]
    public void A_missing_qualification_is_not_the_same_subject_as_a_held_result() =>
        Assert.NotEqual(
            EnquirySubject.ForResult(Result("60172186", "S2024")),
            EnquirySubject.ForQualification(Qualification("60172186")));

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.ResultDoesNotBelong)]
    public void A_result_journey_takes_its_subject_from_the_selected_result(WhatToChange kind)
    {
        var result = Result("60172186", "S2024");
        var journey = new RequestState { SelectedWhatToChange = kind, SelectedResult = result };

        Assert.Equal(EnquirySubject.ForResult(result), EnquirySubject.ForJourney(journey));
    }

    [Fact]
    public void A_missing_qualification_journey_takes_its_subject_from_the_selected_qualification()
    {
        var qualification = Qualification("60172186");
        var journey = new RequestState
        {
            SelectedWhatToChange = WhatToChange.MissingQualification,
            SelectedQualification = qualification
        };

        Assert.Equal(EnquirySubject.ForQualification(qualification), EnquirySubject.ForJourney(journey));
    }

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.MissingQualification)]
    public void A_journey_with_nothing_selected_has_no_subject(WhatToChange kind) =>
        Assert.Null(EnquirySubject.ForJourney(new RequestState { SelectedWhatToChange = kind }));
}
