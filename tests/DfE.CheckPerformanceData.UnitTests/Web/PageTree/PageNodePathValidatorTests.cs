using DfE.CheckPerformanceData.Web.PageTree;
using NSubstitute;
namespace DfE.CheckPerformanceData.Application.UnitTests.Web.PageTree;

public class PageNodePathValidatorTests
{
    private static PageNodePathValidator Sut(params string[] reserved)
    {
        var provider = Substitute.For<IReservedRouteProvider>();
        provider.ReservedFirstSegments().Returns(new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase));
        return new PageNodePathValidator(provider);
    }

    // ── Original cases ────────────────────────────────────────────────────────

    // A truly non-default reserved segment (e.g. "queues") must still be rejected.
    [Fact] public void Rejects_NonDefaultReservedAppRoute()
        => Assert.False(Sut("queues", "pupils").Validate("queues/admin").ok);

    [Fact] public void Rejects_HardReserved()    => Assert.False(Sut().Validate("admin").ok);
    [Fact] public void Rejects_Empty()           => Assert.False(Sut().Validate("").ok);
    [Fact] public void Rejects_BadChars()        => Assert.False(Sut().Validate("Support Page").ok);
    [Fact] public void Allows_FreePath()         => Assert.True(Sut("help").Validate("support/faq").ok);

    // ── Default page-tree roots are allowed even when reserved ────────────────

    // "help" is claimed by a route but is a default root → allowed as a first segment.
    [Fact] public void Allows_Help_EvenIfReserved()
        => Assert.True(Sut("help", "guidance").Validate("help").ok);

    // "guidance" is claimed by a route but is a default root → allowed.
    [Fact] public void Allows_Guidance_EvenIfReserved()
        => Assert.True(Sut("help", "guidance").Validate("guidance").ok);

    // "wiki" and "support" are default roots → allowed whether reserved or not.
    [Fact] public void Allows_Wiki()
        => Assert.True(Sut("help", "guidance").Validate("wiki").ok);

    [Fact] public void Allows_Support()
        => Assert.True(Sut("help", "guidance").Validate("support").ok);

    // Child paths under default roots are allowed even if the root itself is reserved.
    [Fact] public void Allows_ChildOfHelp_EvenIfReserved()
        => Assert.True(Sut("help", "guidance").Validate("help/how-to-submit").ok);

    [Fact] public void Allows_ChildOfGuidance_EvenIfReserved()
        => Assert.True(Sut("help", "guidance").Validate("guidance/my-page").ok);

    [Fact] public void Allows_DeepChildOfGuidance_EvenIfReserved()
        => Assert.True(Sut("help", "guidance").Validate("guidance/section/my-page").ok);

    // ── HardReserved always wins, even for default-root look-alikes ───────────

    // "admin" is hard-reserved, not a default root — still rejected.
    [Fact] public void Rejects_HardReserved_Admin_Unconditionally()
        => Assert.False(Sut().Validate("admin/anything").ok);

    [Fact] public void Rejects_HardReserved_Dev_Unconditionally()
        => Assert.False(Sut().Validate("dev").ok);

    [Fact] public void Rejects_HardReserved_Healthcheck_Unconditionally()
        => Assert.False(Sut().Validate("healthcheck").ok);
}
