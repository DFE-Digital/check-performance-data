using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Review finding 3 on AB#298229: the summary's enquiry branch — the enquiry heading, the no-drafts
// rule and the cancel link — switched on the presence of the per-type summary shapes, and each
// shape's builder guards on one named enum member. That is the recurring bug class on this
// subsystem (the AB#297848 Back link had exactly it): a third enquiry journey would silently render
// as an amendment, with no failing test. Resolved through the checking-exercise map instead,
// mirroring PageViewModel.IsResultsEnquiry and its sweep in PageViewModelResultsEnquiryTests.
public sealed class SummaryViewModelResultsEnquiryTests
{
    private static SummaryViewModel For(WhatToChange change) => new()
    {
        WhatToChange = change,
        PupilName = "Smith, Alice",
        Rows = [],
        FileRows = [],
        BackPageId = "any-page",
        MaxEvidencePages = 0,
        LearnerNoun = null!
    };

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.MissingQualification)]
    public void An_enquiry_summary_is_an_enquiry_even_without_its_summary_shape(WhatToChange change) =>
        // The load-bearing case: no Enquiry/MissingQualification shape set. Shape-presence said
        // "amendment" here; the map says what the journey actually is.
        Assert.True(For(change).IsResultsEnquiry);

    [Theory]
    [InlineData(WhatToChange.Remove)]
    [InlineData(WhatToChange.Include)]
    [InlineData(WhatToChange.Merge)]
    [InlineData(WhatToChange.Add)]
    public void An_amendment_summary_is_not_an_enquiry(WhatToChange change) =>
        Assert.False(For(change).IsResultsEnquiry);

    [Fact]
    public void Every_results_enquiry_exercise_member_is_covered()
    {
        // The pair above is the whole set today. If a third enquiry journey is added and this
        // fails, add it to the Theory rather than deleting this — that is the point.
        var enquiries = Enum.GetValues<WhatToChange>().Where(c => For(c).IsResultsEnquiry);
        Assert.Equal(
            [WhatToChange.IncorrectGrade, WhatToChange.MissingQualification],
            enquiries.ToArray());
    }
}
