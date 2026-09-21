using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// #439: the Include journey's window-type contract. The Include_*.json flows exist only for
/// KS4June — on KS2 and KS4Autumn the radio renders today but the post is a historical dead-end
/// redirect — so this set deliberately matches every window type that currently offers the
/// option and excludes Post16, where inclusion is not a supported journey and no
/// Include_Post16.json exists. Single source of truth for the Include radio on What to change
/// and for the guard on the post it produces (FR-008): a window missing from here must not open
/// the journey even if a flow file is later uploaded for it.
/// </summary>
public static class IncludeJourney
{
    /// <summary>
    /// The window types the Include option is offered for. Single source of truth for the Include
    /// radio on What to change and for the guard on the post it produces — a window missing from
    /// here must not open the journey even if a flow file is later uploaded for it.
    /// </summary>
    public static readonly IReadOnlySet<CheckingWindowType> SupportedWindowTypes =
        new HashSet<CheckingWindowType>
        {
            CheckingWindowType.KS4June,
            CheckingWindowType.KS4Autumn,
            CheckingWindowType.KS2
        };
}