using System.Text.Json;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;
using LearnerNoun = DfE.CheckPerformanceData.Application.WindowManagement.LearnerNoun;

namespace DfE.CheckPerformanceData.Web.Session;

public static class SessionExtensions
{
    private static string Key(Guid windowId) => $"request_{windowId}";

    private const string SelectedWindowIdKey = "SelectedWindowId";
    private const string SelectedWindowTypeKey = "SelectedWindowType";

    /// <summary>
    /// Stamps the window the main nav's window-scoped links point at, together with the type those
    /// links take their learner noun from. The two are always written and cleared together, so the
    /// nav can never label a 16-19 window's link "Pupils".
    /// </summary>
    public static void SetSelectedWindow(this ISession session, Guid windowId, CheckingWindowType type)
    {
        session.SetString(SelectedWindowIdKey, windowId.ToString());
        session.SetString(SelectedWindowTypeKey, type.ToString());
    }

    public static string? GetSelectedWindowId(this ISession session) =>
        session.GetString(SelectedWindowIdKey);

    /// <summary>
    /// The learner noun of the selected window — "student" on 16-19, "pupil" everywhere else.
    /// Falls back to "pupil" when no type is stamped (a session written before the type was, or
    /// no window selected at all), which is the noun every key stage but 16-19 uses.
    /// </summary>
    public static LearnerNoun GetSelectedWindowLearnerNoun(this ISession session) =>
        Enum.TryParse<CheckingWindowType>(session.GetString(SelectedWindowTypeKey), out var type)
            ? LearnerNoun.For(type)
            : LearnerNoun.Pupil;

    public static void ClearSelectedWindow(this ISession session)
    {
        session.Remove(SelectedWindowIdKey);
        session.Remove(SelectedWindowTypeKey);
    }

    public static RequestState GetRequestState(this ISession session, Guid windowId)
    {
        var json = session.GetString(Key(windowId));
        return json is null ? new RequestState() : JsonSerializer.Deserialize<RequestState>(json)!;
    }

    public static void SaveRequestState(this ISession session, Guid windowId, Action<RequestState> update)
    {
        var state = session.GetRequestState(windowId);
        update(state);
        session.SetString(Key(windowId), JsonSerializer.Serialize(state));
    }

    public static void SetRequestState(this ISession session, Guid windowId, RequestState state) =>
        session.SetString(Key(windowId), JsonSerializer.Serialize(state));

    public static void ClearRequestState(this ISession session, Guid windowId) =>
        session.Remove(Key(windowId));

    private static string BulkSelectionKey(Guid windowId) => $"bulk_selection_{windowId}";

    public static void SetBulkSelection(this ISession session, Guid windowId, IReadOnlyList<string> references) =>
        session.SetString(BulkSelectionKey(windowId), JsonSerializer.Serialize(references));

    public static IReadOnlyList<string> GetBulkSelection(this ISession session, Guid windowId)
    {
        var json = session.GetString(BulkSelectionKey(windowId));
        return json is null ? [] : JsonSerializer.Deserialize<List<string>>(json)!;
    }

    public static void ClearBulkSelection(this ISession session, Guid windowId) =>
        session.Remove(BulkSelectionKey(windowId));

    private static string BulkEditModeKey(Guid windowId) => $"bulk_edit_{windowId}";

    /// <summary>
    /// Marks the current journey as having been opened from the bulk review page, so the summary
    /// links back to that page and hides its own submit/save actions (the batch is submitted as a whole).
    /// </summary>
    public static void SetBulkEditMode(this ISession session, Guid windowId) =>
        session.SetString(BulkEditModeKey(windowId), "1");

    public static bool IsBulkEditMode(this ISession session, Guid windowId) =>
        session.GetString(BulkEditModeKey(windowId)) is not null;

    public static void ClearBulkEditMode(this ISession session, Guid windowId) =>
        session.Remove(BulkEditModeKey(windowId));

    private static string SingleEditModeKey(Guid windowId) => $"single_edit_{windowId}";

    /// <summary>
    /// Marks the current journey as having been opened by editing a single request from the
    /// Amendment Requests page, so the summary links back there rather than into the journey.
    /// The submit/save actions remain available (unlike a bulk edit).
    /// </summary>
    public static void SetSingleEditMode(this ISession session, Guid windowId) =>
        session.SetString(SingleEditModeKey(windowId), "1");

    public static bool IsSingleEditMode(this ISession session, Guid windowId) =>
        session.GetString(SingleEditModeKey(windowId)) is not null;

    public static void ClearSingleEditMode(this ISession session, Guid windowId) =>
        session.Remove(SingleEditModeKey(windowId));
}
