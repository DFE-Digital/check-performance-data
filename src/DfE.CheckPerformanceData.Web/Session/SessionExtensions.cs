using System.Text.Json;

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
}
