using System.Reflection;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformance.Persistence.Entities;
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
    [InlineData(nameof(QueueAdminController.RedriveMessage))]
    [InlineData(nameof(QueueAdminController.PurgeMessage))]
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
    [InlineData(nameof(QueueAdminController.RedriveMessage))]
    [InlineData(nameof(QueueAdminController.PurgeMessage))]
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

    // --- A bulk redrive writes an immutable admin-action audit entry (who/what/when) ---

    [Fact]
    public async Task Redrive_WritesImmutableAuditEntry()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin-123");

        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);
        var id = Guid.NewGuid();

        await controller.Redrive(new[] { id });

        // An explicit forensic audit row records the admin action with the actor.
        auditEntries.Received().Add(Arg.Is<AuditEntry>(a =>
            a.Action == "Redrive"
            && a.EntityType == "DlqMessage"
            && a.EntityId.Contains(id.ToString())
            && a.UserId == "admin-123"));
        await dbContext.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // --- Purge writes the audit BEFORE deleting, so the audit survives the purged row ---

    [Fact]
    public async Task Purge_WritesAuditBeforeDeleting_SoItSurvives()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin-456");

        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);
        var id = Guid.NewGuid();

        var auditWritten = false;
        dbContext.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { auditWritten = true; return Task.FromResult(1); });
        adminService
            .When(s => s.PurgeAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()))
            .Do(_ => Assert.True(auditWritten,
                "The audit row must be persisted before the dead-letter rows are purged."));

        await controller.Purge(new[] { id });

        auditEntries.Received().Add(Arg.Is<AuditEntry>(a =>
            a.Action == "Purge"
            && a.EntityType == "DlqMessage"
            && a.EntityId.Contains(id.ToString())
            && a.UserId == "admin-456"));
        await adminService.Received().PurgeAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    // --- The single-id redrive action requeues exactly that message and audits it ---

    [Fact]
    public async Task RedriveMessage_Single_RequeuesAndAudits()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var controller = new QueueAdminController(adminService, dbContext: dbContext);
        var id = Guid.NewGuid();

        await controller.RedriveMessage(id);

        await adminService.Received().RedriveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
        auditEntries.Received().Add(Arg.Is<AuditEntry>(a => a.Action == "Redrive"));
    }

    // --- The single-id purge action purges exactly that message and audits it ---

    [Fact]
    public async Task PurgeMessage_Single_PurgesAndAudits()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var controller = new QueueAdminController(adminService, dbContext: dbContext);
        var id = Guid.NewGuid();

        await controller.PurgeMessage(id);

        await adminService.Received().PurgeAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
            Arg.Any<CancellationToken>());
        auditEntries.Received().Add(Arg.Is<AuditEntry>(a => a.Action == "Purge"));
    }

    // --- Empty / Guid.Empty ids are rejected server-side: no service call, no audit ---

    [Fact]
    public async Task Redrive_RejectsInvalidIds_ServerSide()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var controller = new QueueAdminController(adminService, dbContext: dbContext);

        await controller.Redrive(new[] { Guid.Empty });

        await adminService.DidNotReceive().RedriveAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        auditEntries.DidNotReceive().Add(Arg.Any<AuditEntry>());
    }

    [Fact]
    public async Task Purge_RejectsInvalidIds_ServerSide()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var controller = new QueueAdminController(adminService, dbContext: dbContext);

        await controller.Purge(Array.Empty<Guid>());

        await adminService.DidNotReceive().PurgeAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        auditEntries.DidNotReceive().Add(Arg.Any<AuditEntry>());
    }

    // --- The bare one-arg construction still compiles and works without an audit sink ---

    [Fact]
    public async Task Redrive_WithoutDbContext_StillRedrives_NoNre()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var controller = new QueueAdminController(adminService);
        var id = Guid.NewGuid();

        var result = await controller.Redrive(new[] { id });

        await adminService.Received().RedriveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
        Assert.IsType<RedirectToActionResult>(result);
    }

    private static (IPortalDbContext DbContext, Microsoft.EntityFrameworkCore.DbSet<AuditEntry> Audits)
        SubstituteDbContext()
    {
        var dbContext = Substitute.For<IPortalDbContext>();
        var audits = Substitute.For<Microsoft.EntityFrameworkCore.DbSet<AuditEntry>>();
        dbContext.AuditEntries.Returns(audits);
        dbContext.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        return (dbContext, audits);
    }
}
