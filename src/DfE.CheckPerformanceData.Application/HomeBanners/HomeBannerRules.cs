using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.HomeBanners;

/// <summary>
/// The only code that compares a banner's dates with the clock (#566). Dates are UK wall-clock
/// values and <paramref name="now"/> is <c>TimeProvider.GetLocalNow().DateTime</c>, the clock the
/// checking-exercise gates read, so a banner and an exercise can never disagree about the time.
/// "Show from" is inclusive and "Show until" exclusive, the same as an exercise's VisibleUntil.
/// </summary>
public static class HomeBannerRules
{
    public const int MaxHeadingLength = 200;

    public static HomeBannerStatus StatusOf(bool isEnabled, DateTime? showFrom, DateTime? showUntil, DateTime now)
    {
        if (!isEnabled) return HomeBannerStatus.Off;
        if (showFrom is { } from && now < from) return HomeBannerStatus.Scheduled;
        if (showUntil is { } until && now >= until) return HomeBannerStatus.Expired;
        return HomeBannerStatus.Live;
    }

    public static bool IsLive(bool isEnabled, DateTime? showFrom, DateTime? showUntil, DateTime now) =>
        StatusOf(isEnabled, showFrom, showUntil, now) == HomeBannerStatus.Live;
}
