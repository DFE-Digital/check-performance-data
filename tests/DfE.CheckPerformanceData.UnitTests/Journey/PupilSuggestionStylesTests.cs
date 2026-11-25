using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public sealed class PupilSuggestionStylesTests
{
    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.MissingQualification)]
    [InlineData(WhatToChange.ResultDoesNotBelong)]
    public void A_results_enquiry_journey_gets_the_results_enquiry_row(WhatToChange change)
        => Assert.Equal(PupilSuggestionStyle.ResultsEnquiry, PupilSuggestionStyles.For(change));

    [Theory]
    [InlineData(WhatToChange.Merge)]
    [InlineData(WhatToChange.Include)]
    [InlineData(WhatToChange.Remove)]
    [InlineData(WhatToChange.Add)]
    public void An_amendment_journey_gets_the_standard_row(WhatToChange change)
        => Assert.Equal(PupilSuggestionStyle.Standard, PupilSuggestionStyles.For(change));

    [Fact]
    public void A_journey_with_no_change_type_gets_the_standard_row()
        => Assert.Equal(PupilSuggestionStyle.Standard, PupilSuggestionStyles.For(null));
}
