using DfE.CheckPerformanceData.Web.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class OutcomeKeyValidatorTests
{
    private static readonly string[] Existing = { "Inclusion", "Deceased" };

    [Fact]
    public void Empty_Key_Is_Rejected()
    {
        Assert.Contains(OutcomeKeyValidator.Validate("", Existing), e => e.Contains("Enter an outcome key"));
    }

    [Fact]
    public void Key_With_Spaces_Or_Punctuation_Is_Rejected()
    {
        Assert.NotEmpty(OutcomeKeyValidator.Validate("Bad Key", Existing));
        Assert.NotEmpty(OutcomeKeyValidator.Validate("bad-key", Existing));
    }

    [Fact]
    public void Key_Starting_With_Digit_Is_Rejected()
    {
        Assert.NotEmpty(OutcomeKeyValidator.Validate("9lives", Existing));
    }

    [Fact]
    public void Duplicate_Key_Is_Rejected()
    {
        Assert.Contains(OutcomeKeyValidator.Validate("Inclusion", Existing), e => e.Contains("already exists"));
    }

    [Fact]
    public void Valid_New_Key_Has_No_Errors()
    {
        Assert.Empty(OutcomeKeyValidator.Validate("NewOutcome2", Existing));
    }
}
