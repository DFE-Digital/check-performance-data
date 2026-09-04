using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// AB#297848: which journeys count as a results enquiry for the first-page Back link.
///
/// The regression this guards: the Back link branch tested <c>WhatToChange.IncorrectGrade</c>
/// literally, so the missing-qualification journey — whose first page is cohort-scope with an empty
/// history, and therefore has no BackPageId — fell through to the amendment chooser at
/// WhatToChange/Index, dropping the user into a different task.
/// </summary>
public sealed class PageViewModelResultsEnquiryTests
{
    private static PageViewModel For(WhatToChange? change) => new()
    {
        Page = new JourneyPage { Id = "cohort-scope" },
        Answers = [],
        QuestionModels = [],
        WhatToChange = change
    };

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.MissingQualification)]
    [InlineData(WhatToChange.ResultDoesNotBelong)]
    public void Every_enquiry_journey_goes_back_to_the_result_issue_chooser(WhatToChange change) =>
        Assert.True(For(change).IsResultsEnquiry);

    [Theory]
    [InlineData(WhatToChange.Remove)]
    [InlineData(WhatToChange.Include)]
    [InlineData(WhatToChange.Merge)]
    [InlineData(WhatToChange.Add)]
    public void An_amendment_journey_still_goes_back_to_the_what_to_change_chooser(WhatToChange change) =>
        Assert.False(For(change).IsResultsEnquiry);

    [Fact]
    public void A_journey_with_no_selection_is_not_an_enquiry() =>
        // Defensive: the Back link renders before the guard clauses on some error paths, and a null
        // here must fall to the amendment chooser rather than throw.
        Assert.False(For(null).IsResultsEnquiry);

    [Fact]
    public void Every_results_enquiry_exercise_member_is_covered()
    {
        // The trio above is the whole set today (AB#298704 added the third). If a fourth enquiry
        // journey is added and this fails, add it to the Theory rather than deleting this — that is
        // the point.
        var enquiries = Enum.GetValues<WhatToChange>().Where(c => For(c).IsResultsEnquiry);
        Assert.Equal(
            [WhatToChange.IncorrectGrade, WhatToChange.MissingQualification, WhatToChange.ResultDoesNotBelong],
            enquiries.ToArray());
    }
}
