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

    private static SubmittedRequestViewModel MakeVm(WhatToChange whatToChange) => new()
    {
        LearnerNoun = LearnerNoun.Pupil,
        WindowId = Guid.NewGuid(),
        WhatToChange = whatToChange,
        Status = RequestStatus.SubmittedUnCommitted,
        PupilName = "Alice Newpupil",
        Rows = [],
        Files = [],
        ReferenceNumber = "CYPMD_KS4June_ABC1234"
    };
}
