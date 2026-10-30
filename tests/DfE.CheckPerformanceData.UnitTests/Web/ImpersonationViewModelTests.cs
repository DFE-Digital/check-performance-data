using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationViewModelTests
{
    [Fact]
    public void Missing_ages_are_invalid()
    {
        var model = new ImpersonationViewModel { Laestab = "1230456", Urn = "876543" };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.LowestAge)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(model.HighestAge)));
    }

    [Theory]
    [InlineData("1230456", "876543", 3, 19, true)]
    [InlineData("", "876543", 3, 19, false)]
    [InlineData("1230456", "", 3, 19, false)]
    [InlineData("bad", "876543", 3, 19, false)]
    [InlineData("1230456", "not-a-urn", 3, 19, false)]
    [InlineData("1230456", "876543", -1, 19, false)]
    [InlineData("1230456", "876543", 19, 19, false)]
    [InlineData("1230456", "876543", 20, 19, false)]
    [InlineData("1230456", "876543", 3, 151, false)]
    public void Validates_required_identifiers_formats_and_age_range(
        string laestab, string urn, int lowestAge, int highestAge, bool expected)
    {
        var model = new ImpersonationViewModel
        {
            Laestab = laestab,
            Urn = urn,
            LowestAge = lowestAge,
            HighestAge = highestAge
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.Equal(expected, valid);
    }
}
