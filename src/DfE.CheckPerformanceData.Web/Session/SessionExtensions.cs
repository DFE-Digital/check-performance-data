using System.Text.Json;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Session;

public static class SessionExtensions
{
    private static string Key(Guid windowId) => $"request_{windowId}";

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
}
