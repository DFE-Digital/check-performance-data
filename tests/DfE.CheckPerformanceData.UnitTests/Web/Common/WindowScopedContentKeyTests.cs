using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Common;

// A content block seeds its default text once per key and never again, so a key shared by every
// key stage could only ever hold one learner noun. These pin the suffixing that gives each window
// type its own block — and that the new keys are distinct from the orphaned originals.
public sealed class WindowScopedContentKeyTests
{
    public static TheoryData<CheckingWindowType> AllWindowTypes()
    {
        var data = new TheoryData<CheckingWindowType>();
        foreach (var type in Enum.GetValues<CheckingWindowType>()) data.Add(type);
        return data;
    }

    [Fact]
    public void The_key_is_the_original_suffixed_with_a_lower_case_window_type()
    {
        Assert.Equal("check-pupil-data-title-post16",
            WindowScopedContentKey.For("check-pupil-data-title", CheckingWindowType.Post16));
        Assert.Equal("check-pupil-data-title-ks4june",
            WindowScopedContentKey.For("check-pupil-data-title", CheckingWindowType.KS4June));
    }

    [Theory]
    [MemberData(nameof(AllWindowTypes))]
    public void No_window_types_key_collides_with_the_orphaned_unsuffixed_one(CheckingWindowType type)
    {
        // The unsuffixed block is left in place holding whatever an editor wrote into it. Reusing
        // its key would hand one window type that prose and leave the rest to seed defaults.
        Assert.NotEqual("check-pupil-data-title",
            WindowScopedContentKey.For("check-pupil-data-title", type));
    }

    [Fact]
    public void Every_window_type_gets_a_distinct_key()
    {
        var keys = Enum.GetValues<CheckingWindowType>()
            .Select(t => WindowScopedContentKey.For("check-pupil-data-title", t))
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
