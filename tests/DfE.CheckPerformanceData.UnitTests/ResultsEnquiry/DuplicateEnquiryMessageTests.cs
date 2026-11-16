using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

public sealed class DuplicateEnquiryMessageTests
{
    private const string Reference = "CYPMD_16to19_RE_AAAAAA1";

    private static readonly EnquirySubject HeldResult =
        EnquirySubject.ForResult(new ResultRecord { Qan = "60172186", Session = "S2024" });

    private static readonly EnquirySubject MissingQualification = EnquirySubject.ForQualification(
        new QualificationReference { Qan = "60172186", QualificationTitle = "A made-up qualification" });

    [Fact]
    public void The_message_gives_the_existing_reference_number()
    {
        Assert.Contains(Reference, DuplicateEnquiryMessage.For(HeldResult, Reference));
        Assert.Contains(Reference, DuplicateEnquiryMessage.For(MissingQualification, Reference));
    }

    [Fact]
    public void A_held_result_and_a_missing_qualification_have_their_own_words() =>
        Assert.NotEqual(
            DuplicateEnquiryMessage.For(HeldResult, Reference),
            DuplicateEnquiryMessage.For(MissingQualification, Reference));

    [Fact]
    public void The_message_has_no_qualification_number_or_session()
    {
        var message = DuplicateEnquiryMessage.For(HeldResult, Reference);

        Assert.DoesNotContain("60172186", message);
        Assert.DoesNotContain("S2024", message);
    }
}
