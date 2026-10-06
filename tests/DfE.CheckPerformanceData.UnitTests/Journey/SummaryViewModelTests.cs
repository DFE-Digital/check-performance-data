using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

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

    // #545: the school sees which record the request is about by its CYPMD ID, straight after the
    // name. It has no Change link: the ID changes only by choosing another pupil, which is the
    // name row's link.
    [Fact]
    public void Lines_ForAJourneyWithAPupilSearchPage_ShowTheCypmdIdAfterThePupilName()
    {
        var vm = MakeVm(WhatToChange.Remove, primaryPupilPageId: "select-pupil", pupilCypmdId: "800001");

        var keys = vm.Lines.Select(l => l.Key).ToList();
        Assert.Equal(keys.IndexOf("Pupil name") + 1, keys.IndexOf("CYPMD ID"));
        var line = Assert.Single(vm.Lines, l => l.Key == "CYPMD ID");
        Assert.Equal("800001", line.Value);
        Assert.False(line.HasChange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Lines_WithNoCypmdId_OmitTheCypmdIdRow(string? cypmdId)
    {
        var vm = MakeVm(WhatToChange.Remove, primaryPupilPageId: "select-pupil", pupilCypmdId: cypmdId);

        Assert.DoesNotContain(vm.Lines, l => l.Key == "CYPMD ID");
    }

    // The merge pair replaces the pupil-name row outright, and both of its rows carry links.
    [Fact]
    public void Lines_ForAMergeJourney_KeepBothRecordRowsAndNoPupilNameRow()
    {
        var vm = new SummaryViewModel
        { LearnerNoun = LearnerNoun.Pupil,
            WhatToChange = WhatToChange.Merge,
            PupilName = "Alice Newpupil",
            Rows = [],
            FileRows = [],
            BackPageId = "select-pupil",
            MaxEvidencePages = 0,
            PrimaryPupilPageId = "select-pupil",
            MatchedPupilPageId = "select-match",
            FirstRecordDisplay = "Alice Newpupil, 1 September 2010",
            SecondRecordDisplay = "CY1, Alice Newpupil",
            PupilCypmdId = "800001"
        };

        Assert.DoesNotContain(vm.Lines, l => l.Key == "Pupil name");
        // Each record display carries its own ID, so a separate row would name only one of them.
        Assert.DoesNotContain(vm.Lines, l => l.Key == "CYPMD ID");
        Assert.Contains(vm.Lines, l => l.Key == "First record to merge");
        Assert.Contains(vm.Lines, l => l.Key == "Second record to merge");
    }

    private static SummaryViewModel MakeVm(
        WhatToChange whatToChange, string? primaryPupilPageId = null, string? pupilCypmdId = null) => new()
    {
        LearnerNoun = LearnerNoun.Pupil,
        WhatToChange = whatToChange,
        PupilName = "Alice Newpupil",
        Rows = [],
        FileRows = [],
        BackPageId = "learner-details",
        MaxEvidencePages = 0,
        PrimaryPupilPageId = primaryPupilPageId,
        PupilCypmdId = pupilCypmdId
    };
}
