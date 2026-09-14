using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The file name is a contract with LDS (AB#292610): CYPMD_LDS_{stage}_{type}_YYYY_MM_DD. Pinned
// literally because a wrong stage token or separator is a file LDS silently ignores.
public sealed class EgressOutputTypesTests
{
    [Theory]
    [InlineData(EgressOutputType.NewLearners, WhatToChange.Add)]
    [InlineData(EgressOutputType.RemoveLearners, WhatToChange.Remove)]
    public void Each_output_type_maps_to_exactly_one_amendment_journey(EgressOutputType type, WhatToChange expected)
        => Assert.Equal(expected, EgressOutputTypes.WhatToChangeFor(type));

    [Theory]
    [InlineData(CheckingWindowType.KS4June, "KS4")]
    [InlineData(CheckingWindowType.KS4Autumn, "KS4")]
    [InlineData(CheckingWindowType.KS2, "KS2")]
    [InlineData(CheckingWindowType.Post16, "KS5")]
    public void Stage_token_follows_the_LDS_naming_convention(CheckingWindowType windowType, string expected)
        => Assert.Equal(expected, EgressOutputTypes.StageToken(windowType));

    [Fact]
    public void File_name_is_the_agreed_convention_with_the_export_date()
    {
        var name = EgressOutputTypes.FileName(CheckingWindowType.KS4June, EgressOutputType.RemoveLearners, new DateOnly(2026, 6, 8));
        Assert.Equal("CYPMD_LDS_KS4_RemoveLearners_2026_06_08.csv", name);
    }

    [Fact]
    public void New_learners_file_token_and_correction_type()
    {
        Assert.Equal("CYPMD_LDS_KS2_NewLearners_2026_10_07.csv",
            EgressOutputTypes.FileName(CheckingWindowType.KS2, EgressOutputType.NewLearners, new DateOnly(2026, 10, 7)));
        Assert.Equal("10", EgressOutputTypes.CorrectionType(EgressOutputType.NewLearners));
        Assert.Equal("31", EgressOutputTypes.CorrectionType(EgressOutputType.RemoveLearners));
    }

    [Theory]
    [InlineData("approved", true)]
    [InlineData("auto_approved", true)]
    [InlineData("AUTO_APPROVED", true)]
    [InlineData("rejected", false)]
    [InlineData("auto_rejected", false)]
    [InlineData("scrutiny", false)]
    [InlineData("no-ticket", false)]
    [InlineData(null, false)]
    public void Only_approved_and_auto_approved_pass_the_filter(string? decision, bool expected)
        => Assert.Equal(expected, EgressDecisions.IsApproved(decision));

    [Fact]
    public void Labels_are_human_readable()
    {
        Assert.Equal("Auto approved", EgressDecisions.Label("auto_approved"));
        Assert.Equal("No Zendesk ticket", EgressDecisions.Label(EgressDecisions.NoTicket));
        Assert.Equal("Ticket not found", EgressDecisions.Label(EgressDecisions.NotFound));
        Assert.Equal("Unknown", EgressDecisions.Label(null));
    }

    [Fact]
    public void All_lists_both_supported_types_in_display_order()
        => Assert.Equal([EgressOutputType.NewLearners, EgressOutputType.RemoveLearners], EgressOutputTypes.All);
}
