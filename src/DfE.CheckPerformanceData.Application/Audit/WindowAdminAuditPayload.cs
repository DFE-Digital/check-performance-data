using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// The fields the audit log reads back out of a WindowAdmin audit row's NewValues (AB#301022).
/// WindowRepository.CloseExerciseEarlyAsync writes camelCase JSON (JsonSerializerDefaults.Web).
/// Every member is optional and an unreadable payload is null — the row still lists, with the
/// sign-in subject for a name and no exercise. The payload holds no pupil data: it names a window,
/// an exercise, two dates and the admin.
/// </summary>
public sealed record WindowAdminAuditPayload(Guid? WindowId, string? ExerciseType, DateTime? ScheduledEnd, string? ClosedBy)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WindowAdminAuditPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<WindowAdminAuditPayload>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
