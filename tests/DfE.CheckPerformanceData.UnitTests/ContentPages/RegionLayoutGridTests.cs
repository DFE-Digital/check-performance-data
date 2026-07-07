using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// A region's layout maps to a fixed set of GDS grid column classes — one per column. The mapping
// is the renderer's contract, and is the closed allow-list that keeps layouts safe (no arbitrary CSS).
public class RegionLayoutGridTests
{
    [Fact]
    public void Single_IsOneFullColumn()
    {
        Assert.Equal(["govuk-grid-column-full"], RegionLayoutGrid.ColumnClasses(RegionLayout.Single));
    }

    [Fact]
    public void Halves_AreTwoOneHalfColumns()
    {
        Assert.Equal(
            ["govuk-grid-column-one-half", "govuk-grid-column-one-half"],
            RegionLayoutGrid.ColumnClasses(RegionLayout.Halves));
    }

    [Fact]
    public void Thirds_AreThreeOneThirdColumns()
    {
        Assert.Equal(
            ["govuk-grid-column-one-third", "govuk-grid-column-one-third", "govuk-grid-column-one-third"],
            RegionLayoutGrid.ColumnClasses(RegionLayout.Thirds));
    }

    [Fact]
    public void Quarters_AreFourOneQuarterColumns()
    {
        Assert.Equal(
            [
                "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter",
                "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter"
            ],
            RegionLayoutGrid.ColumnClasses(RegionLayout.Quarters));
    }

    [Fact]
    public void Asymmetric_Layouts_PreserveColumnOrder()
    {
        Assert.Equal(
            ["govuk-grid-column-one-third", "govuk-grid-column-two-thirds"],
            RegionLayoutGrid.ColumnClasses(RegionLayout.OneThirdTwoThirds));

        Assert.Equal(
            ["govuk-grid-column-two-thirds", "govuk-grid-column-one-third"],
            RegionLayoutGrid.ColumnClasses(RegionLayout.TwoThirdsOneThird));
    }

    [Theory]
    [InlineData(RegionLayout.Single, 1)]
    [InlineData(RegionLayout.Halves, 2)]
    [InlineData(RegionLayout.Thirds, 3)]
    [InlineData(RegionLayout.Quarters, 4)]
    [InlineData(RegionLayout.OneThirdTwoThirds, 2)]
    [InlineData(RegionLayout.TwoThirdsOneThird, 2)]
    public void ColumnCount_MatchesClassCount(RegionLayout layout, int expected)
    {
        Assert.Equal(expected, RegionLayoutGrid.ColumnCount(layout));
        Assert.Equal(expected, RegionLayoutGrid.ColumnClasses(layout).Count);
    }
}
