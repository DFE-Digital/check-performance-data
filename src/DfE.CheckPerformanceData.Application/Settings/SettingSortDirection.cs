namespace DfE.CheckPerformanceData.Application.Settings;

// How the settings list on the admin settings page is ordered. URL-driven, no persistence —
// the query string is the source of truth so a bookmarkable link reproduces the same view.
public enum SettingSortDirection
{
    KeyAscending,
    KeyDescending
}
