using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class OutcomeDeletionPolicyTests
{
    [Fact]
    public void Form_Bound_Outcome_Is_Detected()
    {
        Assert.True(OutcomeDeletionPolicy.IsFormBound("Inclusion"));
        Assert.True(OutcomeDeletionPolicy.IsFormBound("ElectiveHomeEducation"));
    }

    [Fact]
    public void Unmapped_Outcome_Is_Not_Form_Bound()
    {
        Assert.False(OutcomeDeletionPolicy.IsFormBound("SomeAdminAddedOutcome"));
    }

    [Fact]
    public void Match_Is_Case_Insensitive()
    {
        Assert.True(OutcomeDeletionPolicy.IsFormBound("inclusion"));
    }
}
