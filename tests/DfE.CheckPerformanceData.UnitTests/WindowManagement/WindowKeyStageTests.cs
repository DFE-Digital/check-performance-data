using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.WindowManagement;

// The key stage is derived from the window type and the wizard no longer asks for it. The map has
// no default case, so these tests make a new CheckingWindowType fail here until it is given one.
public sealed class WindowKeyStageTests
{
    public static TheoryData<CheckingWindowType> AllWindowTypes()
    {
        var data = new TheoryData<CheckingWindowType>();
        foreach (var type in Enum.GetValues<CheckingWindowType>()) data.Add(type);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllWindowTypes))]
    public void Every_window_type_has_a_key_stage(CheckingWindowType type)
    {
        Assert.True(Enum.IsDefined(WindowKeyStage.For(type)));
    }

    [Theory]
    [InlineData(CheckingWindowType.KS2, KeyStages.KS2)]
    [InlineData(CheckingWindowType.KS4June, KeyStages.KS4)]
    [InlineData(CheckingWindowType.KS4Autumn, KeyStages.KS4)]
    [InlineData(CheckingWindowType.Post16, KeyStages.Post16)]
    public void Each_window_type_maps_to_its_key_stage(CheckingWindowType type, KeyStages expected)
    {
        Assert.Equal(expected, WindowKeyStage.For(type));
    }

    [Fact]
    public void An_unknown_window_type_throws_rather_than_guessing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowKeyStage.For((CheckingWindowType)999));
    }
}
