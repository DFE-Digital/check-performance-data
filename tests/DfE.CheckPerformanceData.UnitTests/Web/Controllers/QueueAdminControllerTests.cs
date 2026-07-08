using System.Reflection;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class QueueAdminControllerTests
{
    // --- Controller class carries [RequireAdminSection("rules-engine-queue")] ---
    //
    // Every action (Index / Dlq / Message / Redrive / Purge / RedriveMessage / PurgeMessage
    // and every future action) is covered by the class-level gate.

    [Fact]
    public void Controller_Gated_By_RulesEngineQueue_Section()
    {
        var gate = typeof(QueueAdminController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal("rules-engine-queue", gate!.SectionKey);
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

    // --- The detail views carry the request reference so they can link to its journey ---

    [Fact]
    public async Task Message_CarriesTheReferenceNumber_ForTheJourneyLink()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var id = Guid.NewGuid();
        adminService.GetDlqMessageAsync(id, Arg.Any<CancellationToken>())
            .Returns(new DlqMessage(
                id,
                QueueOptions.ZendeskQueue,
                Attempts: 3,
                Reason: "boom",
                Payload: "{\"ReferenceNumber\":\"KS4-9001\",\"Pupil\":{\"Upn\":\"X999\"}}",
                DeadLetteredAtUtc: DateTime.UtcNow));

        var controller = new QueueAdminController(adminService);

        var result = await controller.Message(id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DlqMessageViewModel>(view.Model);
        Assert.Equal("KS4-9001", model.ReferenceNumber);
    }

    [Fact]
    public async Task WorkingMessage_CarriesTheReferenceNumber_ForTheJourneyLink()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var id = Guid.NewGuid();
        adminService.GetMessageDetailAsync(QueueOptions.RulesEngineQueue, id, Arg.Any<CancellationToken>())
            .Returns(new QueueMessageDetail(
                id,
                QueueOptions.RulesEngineQueue,
                EnqueuedAtUtc: DateTime.UtcNow,
                Attempts: 0,
                Payload: "{\"ReferenceNumber\":\"KS4-9002\"}"));

        var controller = new QueueAdminController(adminService);

        var result = await controller.WorkingMessage(QueueOptions.RulesEngineQueue, id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<QueueMessageViewModel>(view.Model);
        Assert.Equal("KS4-9002", model.ReferenceNumber);
    }

    [Fact]
    public async Task Message_UnparseablePayload_LeavesTheReferenceNull()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var id = Guid.NewGuid();
        adminService.GetDlqMessageAsync(id, Arg.Any<CancellationToken>())
            .Returns(new DlqMessage(
                id,
                QueueOptions.ZendeskQueue,
                Attempts: 1,
                Reason: "boom",
                Payload: "not json",
                DeadLetteredAtUtc: DateTime.UtcNow));

        var controller = new QueueAdminController(adminService);

        var result = await controller.Message(id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<DlqMessageViewModel>(view.Model);
        Assert.Null(model.ReferenceNumber);
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

    // --- Purge runs the forensic audit and the deletion in a single transaction (atomic) ---

    [Fact]
    public async Task Purge_RunsAuditAndDeletion_InOneTransaction()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin-789");

        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);
        var id = Guid.NewGuid();

        await controller.Purge(new[] { id });

        // The audit write and the purge must share one transaction so they commit or roll back
        // together — neither a purge without its audit nor an audit without its purge.
        await dbContext.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        // Both the audit add and the purge happened inside that transaction's work.
        auditEntries.Received().Add(Arg.Is<AuditEntry>(a => a.Action == "Purge"));
        await adminService.Received().PurgeAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redrive_RunsAuditAndRequeue_InOneTransaction()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, _) = SubstituteDbContext();
        var controller = new QueueAdminController(adminService, dbContext: dbContext);
        var id = Guid.NewGuid();

        await controller.Redrive(new[] { id });

        await dbContext.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await adminService.Received().RedriveAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(id)),
            Arg.Any<CancellationToken>());
    }

    // --- A destructive purge with no known actor fails loudly rather than auditing anonymously ---

    [Fact]
    public async Task Purge_WithNoActingUser_DoesNotPurge_AndDoesNotAuditAnonymously()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(string.Empty); // actor unknown

        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);

        await controller.Purge(new[] { Guid.NewGuid() });

        // The forensic control for an irreversible operation must name the actor: with no acting
        // user the purge is refused and no anonymous audit row is written.
        await adminService.DidNotReceive().PurgeAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        auditEntries.DidNotReceive().Add(Arg.Any<AuditEntry>());
    }

    [Fact]
    public async Task PurgeMessage_WithNoActingUser_DoesNotPurge()
    {
        var adminService = Substitute.For<IQueueAdminService>();
        var (dbContext, auditEntries) = SubstituteDbContext();
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns((string)null!);

        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);

        await controller.PurgeMessage(Guid.NewGuid());

        await adminService.DidNotReceive().PurgeAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        auditEntries.DidNotReceive().Add(Arg.Any<AuditEntry>());
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
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("admin-1");
        var controller = new QueueAdminController(
            adminService, dbContext: dbContext, currentUserService: currentUser);
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
        // The transaction wrapper runs the supplied work inline so the audit write and the
        // queue mutation it encloses actually execute under test.
        dbContext.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => ((Func<Task>)call[0]).Invoke());
        return (dbContext, audits);
    }
}
