using System.Text.Json;

namespace DfE.CheckPerformanceData.Web.Session;

public static class SessionExtensions
{
    private static string Key(Guid windowId) => $"journey_{windowId}";

    public static JourneyState GetJourneyState(this ISession session, Guid windowId)
    {
        var json = session.GetString(Key(windowId));
        return json is null ? new JourneyState() : JsonSerializer.Deserialize<JourneyState>(json)!;
    }

    public static void SaveJourneyState(this ISession session, Guid windowId, Action<JourneyState> update)
    {
        var state = session.GetJourneyState(windowId);
        update(state);
        session.SetString(Key(windowId), JsonSerializer.Serialize(state));
    }
}
