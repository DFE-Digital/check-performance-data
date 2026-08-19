using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Controllers.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297310: WhatToChangeNoun/WhatToChangeLabel had no case for WhatToChange.Add, so the summary
// page's "Check details for the {noun} of {pupilName}" heading fell through to the enum's raw,
// lower-cased name — observed live as "Check details for the add of Alice Newpupil".
public sealed class SummaryViewModelTests
{
    [Fact]
    public void WhatToChangeNoun_ForAdd_ReadsAsAddition()
    {
        var vm = MakeVm(WhatToChange.Add);

        Assert.Equal("addition", vm.WhatToChangeNoun);
    }

    [Fact]
    public void WhatToChangeLabel_ForAdd_MatchesTheWhatToChangeRadioLabel()
    {
        var vm = MakeVm(WhatToChange.Add);

        Assert.Equal("Add a pupil to data", vm.WhatToChangeLabel);
    }

    private static SummaryViewModel MakeVm(WhatToChange whatToChange) => new()
    {
        WhatToChange = whatToChange,
        PupilName = "Alice Newpupil",
        Rows = [],
        FileRows = [],
        BackPageId = "learner-details",
        MaxEvidencePages = 0
    };
}
