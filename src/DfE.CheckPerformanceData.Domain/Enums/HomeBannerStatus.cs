namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>What the admin list says about a start-page banner right now (#566).</summary>
public enum HomeBannerStatus
{
    /// <summary>The switch is off. Off always hides the banner, whatever the dates say.</summary>
    Off,
    /// <summary>On, and "Show from" is still in the future.</summary>
    Scheduled,
    /// <summary>On, and now is within the dates. Shown on the start page.</summary>
    Live,
    /// <summary>On, and "Show until" has passed.</summary>
    Expired
}
