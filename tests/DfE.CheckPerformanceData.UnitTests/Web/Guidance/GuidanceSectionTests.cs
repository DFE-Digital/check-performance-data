using DfE.CheckPerformanceData.Web.Models.Guidance;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

public sealed class GuidanceSectionTests
{
    [Fact]
    public void Level2Section_RendersH2_WithHeadingL()
    {
        var section = MakeSection(level: 2);

        Assert.Equal("h2", section.HeadingElement);
        Assert.Equal("govuk-heading-l", section.HeadingCssClass);
    }

    [Fact]
    public void Level3Section_RendersH3_WithHeadingM()
    {
        var section = MakeSection(level: 3);

        Assert.Equal("h3", section.HeadingElement);
        Assert.Equal("govuk-heading-m", section.HeadingCssClass);
    }

    [Fact]
    public void DefaultLevel_IsTopLevel_H2()
    {
        var section = new GuidanceSection
        {
            Anchor = "x",
            NavTitle = "X",
            HeadingBlockKey = "k-heading",
            BlockKey = "k"
        };

        Assert.Equal(2, section.Level);
        Assert.Equal("h2", section.HeadingElement);
    }

    [Fact]
    public void Ks4Manifest_EveryHeadingKey_IsBodyKeyPlusHeadingSuffix()
    {
        // GuidancePage's section rendering derives each heading anchor by stripping the
        // "-heading" suffix off the heading block key, so this suffix relationship is a
        // contract the guidance section rendering depends on.
        Assert.All(GuidancePage.Ks4June2026.Sections,
            s => Assert.Equal(s.BlockKey + "-heading", s.HeadingBlockKey));
    }

    private static GuidanceSection MakeSection(int level) => new()
    {
        Anchor = "pupil-removal-reason",
        NavTitle = "Pupil removal reason",
        Level = level,
        HeadingBlockKey = "guidance-ks4-2026-pupil-removal-reason-heading",
        BlockKey = "guidance-ks4-2026-pupil-removal-reason"
    };
}
