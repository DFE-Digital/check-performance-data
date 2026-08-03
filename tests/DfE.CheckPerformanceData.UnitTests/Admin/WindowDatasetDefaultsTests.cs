using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// A Post16 window ingests TWO supplier files (included + non-included) because the non-included
// file has no P_INCL column; every other window type ingests one.
public class WindowDatasetDefaultsTests
{
    [Fact]
    public void Post16_defaults_to_an_included_and_a_non_included_dataset()
    {
        var datasets = WindowDatasets.DefaultsFor(CheckingWindowType.Post16);

        Assert.Equal(2, datasets.Count);
        Assert.Equal("included", datasets[0].Name);
        Assert.True(datasets[0].Included);
        Assert.Equal(0, datasets[0].SortOrder);
        Assert.Equal("nonincluded", datasets[1].Name);
        Assert.False(datasets[1].Included);
        Assert.Equal(1, datasets[1].SortOrder);
    }

    [Theory]
    [InlineData(CheckingWindowType.KS4June)]
    [InlineData(CheckingWindowType.KS4Autumn)]
    [InlineData(CheckingWindowType.KS2)]
    public void Other_window_types_default_to_one_dataset_with_no_stamped_inclusion(CheckingWindowType type)
    {
        var datasets = WindowDatasets.DefaultsFor(type);

        var only = Assert.Single(datasets);
        Assert.Equal("pupils", only.Name);
        Assert.Null(only.Included);
    }
}
