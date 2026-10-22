using System.Text.Json;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Audit;

// AB#302158: the row the service writes when it hands an exercise's requests over by itself.
// Audit rows cannot be deleted (the immutability trigger), so the fact isolates by its own
// window id. The window need not exist: the row's EntityId is the id as text.
[Collection(nameof(PostgresCollection))]
public sealed class WindowAdminAuditWriterTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_automatic_hand_over_is_one_window_admin_row_with_no_user_and_the_counts()
    {
        var windowId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var ranAtUtc = new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc);

        await using (var ctx = fixture.CreateContext())
        {
            await new WindowAdminAuditWriter(ctx).RecordAutomaticHandOverAsync(new AutomaticHandOverAudit
            {
                WindowId = windowId,
                WindowTitle = "Key Stage 4 June",
                ExerciseId = exerciseId,
                Exercise = CheckingExerciseType.PupilData,
                ExerciseEnd = new DateTime(2026, 11, 2, 17, 0, 0),
                RequestsSent = 3,
                DraftsCancelled = 2,
                RanAtUtc = ranAtUtc
            }, CancellationToken.None);
        }

        await using var read = fixture.CreateContext();
        var windowText = windowId.ToString();
        var row = Assert.Single(await read.AuditEntries.AsNoTracking()
            .Where(a => a.EntityType == AuditActivities.WindowAdmin && a.EntityId == windowText)
            .ToListAsync());

        Assert.Equal(AuditActivities.RequestsSentAutomaticallyAction, row.Action);
        // No person did this. A null user is what the audit log shows as "System".
        Assert.Null(row.UserId);
        Assert.Equal(ranAtUtc, row.Timestamp);

        using var payload = JsonDocument.Parse(row.NewValues!);
        var root = payload.RootElement;
        Assert.Equal(windowId, root.GetProperty("windowId").GetGuid());
        Assert.Equal("Key Stage 4 June", root.GetProperty("windowTitle").GetString());
        Assert.Equal(exerciseId, root.GetProperty("exerciseId").GetGuid());
        Assert.Equal("PupilData", root.GetProperty("exerciseType").GetString());
        Assert.Equal(new DateTime(2026, 11, 2, 17, 0, 0), root.GetProperty("exerciseEnd").GetDateTime());
        Assert.Equal(3, root.GetProperty("requestsSent").GetInt32());
        Assert.Equal(2, root.GetProperty("draftsCancelled").GetInt32());
        Assert.True(root.GetProperty("automatic").GetBoolean());
    }

    [Fact]
    public async Task An_extensions_hand_over_carries_the_extension_id_and_the_exercises_own_carries_none()
    {
        var extensionId = Guid.NewGuid();
        var ownWindow = Guid.NewGuid();
        var extensionWindow = Guid.NewGuid();

        await using (var ctx = fixture.CreateContext())
        {
            var writer = new WindowAdminAuditWriter(ctx);
            await writer.RecordAutomaticHandOverAsync(HandOver(extensionWindow, extensionId), CancellationToken.None);
            await writer.RecordAutomaticHandOverAsync(HandOver(ownWindow, null), CancellationToken.None);
        }

        await using var read = fixture.CreateContext();
        var extensionText = extensionWindow.ToString();
        var ownText = ownWindow.ToString();
        var extensionRow = Assert.Single(await read.AuditEntries.AsNoTracking().Where(a => a.EntityId == extensionText).ToListAsync());
        var ownRow = Assert.Single(await read.AuditEntries.AsNoTracking().Where(a => a.EntityId == ownText).ToListAsync());

        using var extensionPayload = JsonDocument.Parse(extensionRow.NewValues!);
        Assert.Equal(extensionId, extensionPayload.RootElement.GetProperty("extensionId").GetGuid());
        using var ownPayload = JsonDocument.Parse(ownRow.NewValues!);
        Assert.Equal(JsonValueKind.Null, ownPayload.RootElement.GetProperty("extensionId").ValueKind);
    }

    private static AutomaticHandOverAudit HandOver(Guid windowId, Guid? extensionId) => new()
    {
        WindowId = windowId,
        WindowTitle = "Key Stage 4 June",
        ExerciseId = Guid.NewGuid(),
        Exercise = CheckingExerciseType.PupilData,
        ExerciseEnd = new DateTime(2026, 11, 2, 17, 0, 0),
        RequestsSent = 1,
        DraftsCancelled = 0,
        RanAtUtc = new DateTime(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc),
        ExtensionId = extensionId
    };
}
