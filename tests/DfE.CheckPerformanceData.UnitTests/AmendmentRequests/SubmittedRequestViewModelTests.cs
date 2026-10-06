using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

// AB#297310: same gap as SummaryViewModel — WhatToChangeNoun/WhatToChangeLabel had no case for
// WhatToChange.Add, so the read-only submitted-request view's heading fell through to the enum's
// raw, lower-cased name ("add") instead of a readable noun.
public sealed class SubmittedRequestViewModelTests
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

    // #545: the submitted view names the pupil's CYPMD ID after the name, as the summary does.
    [Fact]
    public void ShowCypmdIdRow_ForASinglePupilRequestWithAnId_IsTrue()
    {
        var vm = MakeVm(WhatToChange.Remove, pupilCypmdId: "800001");

        Assert.True(vm.ShowCypmdIdRow);
    }

    // An Add request names a pupil not yet in the data, so there is no ID to show.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ShowCypmdIdRow_WithNoId_IsFalse(string? cypmdId)
    {
        var vm = MakeVm(WhatToChange.Add, pupilCypmdId: cypmdId);

        Assert.False(vm.ShowCypmdIdRow);
    }

    // Each merge record display carries its own ID.
    [Fact]
    public void ShowCypmdIdRow_ForAMerge_IsFalse()
    {
        var vm = MakeVm(WhatToChange.Merge, pupilCypmdId: "800001", secondRecordDisplay: "Casey Carter (800002)");

        Assert.False(vm.ShowCypmdIdRow);
    }

    private static SubmittedRequestViewModel MakeVm(
        WhatToChange whatToChange, string? pupilCypmdId = null, string? secondRecordDisplay = null) => new()
    {
        LearnerNoun = LearnerNoun.Pupil,
        WindowId = Guid.NewGuid(),
        WhatToChange = whatToChange,
        Status = RequestStatus.Submitted,
        PupilName = "Alice Newpupil",
        Rows = [],
        Files = [],
        ReferenceNumber = "CYPMD_KS4June_ABC1234",
        PupilCypmdId = pupilCypmdId,
        SecondRecordDisplay = secondRecordDisplay
    };
}
