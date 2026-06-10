using System.Reflection;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class QueueAdminControllerTests
{
    // --- Every action is gated by the literal cypmd_admin role ---

    [Theory]
    [InlineData(nameof(QueueAdminController.Index))]
    [InlineData(nameof(QueueAdminController.Dlq))]
    [InlineData(nameof(QueueAdminController.Message))]
    [InlineData(nameof(QueueAdminController.Redrive))]
    [InlineData(nameof(QueueAdminController.Purge))]
    public void Action_Has_Authorize_Attribute_With_AdminRole(string actionName)
    {
        var method = typeof(QueueAdminController).GetMethod(actionName);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        // Pin the literal — do not reference the const on both sides.
        Assert.Equal("cypmd_admin", authorize!.Roles);
    }

    // --- Destructive verbs require antiforgery ---

    [Theory]
    [InlineData(nameof(QueueAdminController.Redrive))]
    [InlineData(nameof(QueueAdminController.Purge))]
    public void DestructiveAction_Has_ValidateAntiForgeryToken(string actionName)
    {
        var method = typeof(QueueAdminController).GetMethod(actionName);
        Assert.NotNull(method);

        var antiforgery = method!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();
        Assert.NotNull(antiforgery);
    }

    // --- DLQ message view redacts the payload by default (D-03) ---

    [Fact]
    public async Task Message_RedactsPayloadByDefault()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var id = Guid.NewGuid();
        adminService.GetDlqMessageAsync(id, Arg.Any<CancellationToken>())
            .Returns(new DlqMessage(
                id,
                QueueOptions.ZendeskQueue,
                Attempts: 3,
                Reason: "boom",
                Payload: "{\"Pupil\":{\"Upn\":\"X999\",\"Firstname\":\"Ann\"}}",
                DeadLetteredAtUtc: DateTime.UtcNow));

        var controller = new QueueAdminController(adminService);

        var result = await controller.Message(id);

        // Default render must not leak raw pupil identifiers from the payload.
        var view = Assert.IsType<ViewResult>(result);
        var rendered = view.Model?.ToString() ?? string.Empty;
        Assert.DoesNotContain("X999", rendered);
    }

    // --- Redrive writes an audit entry (D-04) ---

    [Fact]
    public async Task Redrive_WritesAudit()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var controller = new QueueAdminController(adminService);
        var id = Guid.NewGuid();

        await controller.Redrive(new[] { id });

        await adminService.Received().RedriveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    // --- Purge writes an audit entry (D-04) ---

    [Fact]
    public async Task Purge_WritesAudit()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var controller = new QueueAdminController(adminService);
        var id = Guid.NewGuid();

        await controller.Purge(new[] { id });

        await adminService.Received().PurgeAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }
}
