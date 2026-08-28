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

    // The "Pupil name" row's Change link goes to the pupil-search page. The Add journey has none,
    // so the row rendered with no link at all, directly above the first and last name rows that
    // hold the same name and do have one.
    [Fact]
    public void Lines_ForAJourneyWithNoPupilSearchPage_OmitTheActionlessPupilNameRow()
    {
        var vm = MakeVm(WhatToChange.Add);

        Assert.DoesNotContain(vm.Lines, l => l.Key == "Pupil name");
    }

    [Fact]
    public void Lines_ForAJourneyWithAPupilSearchPage_KeepThePupilNameRow()
    {
        var vm = MakeVm(WhatToChange.Remove, primaryPupilPageId: "select-pupil");

        var line = Assert.Single(vm.Lines, l => l.Key == "Pupil name");
        Assert.Equal("Alice Newpupil", line.Value);
        Assert.True(line.HasChange);
    }

    // The merge pair replaces the pupil-name row outright, and both of its rows carry links.
    [Fact]
    public void Lines_ForAMergeJourney_KeepBothRecordRowsAndNoPupilNameRow()
    {
        var vm = new SummaryViewModel
        {
            WhatToChange = WhatToChange.Merge,
            PupilName = "Alice Newpupil",
            Rows = [],
            FileRows = [],
            BackPageId = "select-pupil",
            MaxEvidencePages = 0,
            PrimaryPupilPageId = "select-pupil",
            MatchedPupilPageId = "select-match",
            FirstRecordDisplay = "Alice Newpupil, 1 September 2010",
            SecondRecordDisplay = "CY1, Alice Newpupil"
        };

        Assert.DoesNotContain(vm.Lines, l => l.Key == "Pupil name");
        Assert.Contains(vm.Lines, l => l.Key == "First record to merge");
        Assert.Contains(vm.Lines, l => l.Key == "Second record to merge");
    }

    private static SummaryViewModel MakeVm(WhatToChange whatToChange, string? primaryPupilPageId = null) => new()
    {
        WhatToChange = whatToChange,
        PupilName = "Alice Newpupil",
        Rows = [],
        FileRows = [],
        BackPageId = "learner-details",
        MaxEvidencePages = 0,
        PrimaryPupilPageId = primaryPupilPageId
    };
}
