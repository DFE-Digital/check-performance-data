namespace DfE.CheckPerformanceData.Application.Analytics;

// One definition of how long search-analytics rows actually survive.
//
// Two places need this and must agree: the retention job, which decides what to purge, and
// the dashboard's prior-window comparison, which decides whether a comparison window still
// has data behind it. When those two disagree the dashboard either hides valid comparison
// chips or renders deltas against rows that have already been purged — a drop in history
// that reads as a surge in traffic.
public static class SearchAnalyticsRetention
{
    // Hard caps applied in code so a mis-configured admin setting cannot disable the
    // ceiling or collapse the window to nothing.
    public const int EventsMinDays = 1;
    public const int EventsMaxDays = 365;

    public const int MessagesMinDays = 1;
    public const int MessagesMaxDays = 730;

    public static int ClampEventDays(int configured) =>
        Math.Clamp(configured, EventsMinDays, EventsMaxDays);

    public static int ClampMessageDays(int configured) =>
        Math.Clamp(configured, MessagesMinDays, MessagesMaxDays);
}
