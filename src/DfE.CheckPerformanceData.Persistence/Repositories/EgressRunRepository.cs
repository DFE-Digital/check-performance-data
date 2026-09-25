using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class EgressRunRepository(IPortalDbContext db) : IEgressRunRepository
{
    private const string UniqueViolation = "23505";
    // Nit: match the specific constraint, not any 23505 — a coincidental unrelated unique
    // violation must not be misreported as "another run holds this pair".
    private const string ActiveWindowOutputConstraint = "ix_egress_run_outputs_active_window_output";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<EgressBlocker?> FindBlockerAsync(Guid windowId, EgressOutputType outputType, CancellationToken ct) =>
        await db.EgressRunOutputs.AsNoTracking()
            .Where(o => o.WindowId == windowId && o.OutputType == outputType && o.IsActive)
            .Join(db.EgressRuns.AsNoTracking(), o => o.RunId, r => r.Id,
                (o, r) => new EgressBlocker(r.Id, r.Status, r.StartedByName, r.StartedAtUtc, r.TransferredAtUtc, r.TransferredByName))
            .FirstOrDefaultAsync(ct);

    // Every amendment of the type the window holds, whatever its ticket state: a row with no CrmId
    // is shown as "No Zendesk ticket" and filtered out later, not hidden here — the ops user must
    // be able to see that a request exists that scrutiny never saw. Withdrawn/draft rows are not
    // amendments any more.
    public async Task<IReadOnlyList<EgressCandidateRequest>> GetCandidateRequestsAsync(Guid windowId, WhatToChange amendmentType, CancellationToken ct) =>
        await db.ChangeRequests.AsNoTracking()
            .Where(r => r.WindowId == windowId
                && r.RequestType == RequestType.Amendment
                && r.AmendmentType == amendmentType
                && (r.Status == RequestStatus.SubmittedCommitted || r.Status == RequestStatus.SubmittedUnCommitted))
            .OrderBy(r => r.Submitted)
            .Select(r => new EgressCandidateRequest(r.Id, r.ReferenceNumber, r.CrmId, r.OrganisationUrn, r.OrganisationLaestab,
                DateTime.SpecifyKind(r.Submitted, DateTimeKind.Utc), r.Status))
            .ToListAsync(ct);

    public async Task<Guid> CreateRunAsync(EgressRunCreate create, CancellationToken ct)
    {
        var run = new EgressRun
        {
            Id = Guid.NewGuid(), WindowId = create.WindowId, Status = EgressRunStatus.Pulled,
            StartedById = create.StartedById, StartedByName = create.StartedByName, StartedByEmail = create.StartedByEmail,
            StartedAtUtc = DateTime.UtcNow,
            Outputs = create.Outputs.Select(o => new EgressRunOutput
            {
                Id = Guid.NewGuid(), WindowId = create.WindowId, OutputType = o.OutputType, IsActive = true,
                RawRecordsJson = JsonSerializer.Serialize(o.Records, Json), SourceRecordCount = o.Records.Count
            }).ToList()
        };
        db.EgressRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: ActiveWindowOutputConstraint })
        {
            throw new EgressRunConflictException("Another egress run already holds this checking window and output type.");
        }
        return run.Id;
    }

    public async Task<EgressRunDto?> GetRunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.EgressRuns.AsNoTracking().Include(r => r.Outputs).FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return null;
        return new EgressRunDto(run.Id, run.WindowId, run.Status, run.StartedById, run.StartedByName, run.StartedAtUtc,
            run.PreprocessedAtUtc, run.ExportDate, run.TransferredAtUtc, run.TransferredByName,
            run.FailureJson is null ? [] : JsonSerializer.Deserialize<List<EgressRecordFailure>>(run.FailureJson, Json) ?? [],
            run.TransferFailureReason,
            run.Outputs.OrderBy(o => o.OutputType).Select(o => new EgressRunOutputDto(o.Id, o.OutputType, o.IsActive,
                JsonSerializer.Deserialize<List<EgressSourceRecord>>(o.RawRecordsJson, Json) ?? [],
                o.SourceRecordCount, o.OutputRecordCount, o.FileName, o.Sha256)).ToList());
    }

    public async Task<IReadOnlyList<EgressRunListItem>> ListRunsAsync(CancellationToken ct)
    {
        var rows = await db.EgressRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => new
            {
                r.Id, r.WindowId, r.Status, r.StartedByName, r.StartedAtUtc, r.TransferredAtUtc,
                WindowTitle = db.CheckingWindows.Where(w => w.Id == r.WindowId).Select(w => w.Title).FirstOrDefault() ?? string.Empty,
                Types = r.Outputs.OrderBy(o => o.OutputType).Select(o => o.OutputType).ToList()
            })
            .ToListAsync(ct);
        return rows.Select(r => new EgressRunListItem(r.Id, r.WindowId, r.WindowTitle, r.Status, r.Types, r.StartedByName, r.StartedAtUtc, r.TransferredAtUtc)).ToList();
    }

    // Runs history (AB#294590). Filter, count, order and page in SQL — the Pull page's
    // ListRunsAsync loads everything because it shows a handful of live runs; this list grows for
    // ever. The zero-unless-transferred rule is applied after materialisation so the projection
    // stays a plain translatable shape.
    public async Task<EgressRunHistoryPage> ListHistoryAsync(EgressRunHistoryFilter filter, int page, int pageSize, CancellationToken ct)
    {
        if (pageSize < 1) pageSize = 1;
        var query = db.EgressRuns.AsNoTracking();
        if (filter.WindowId is { } windowId)
            query = query.Where(r => r.WindowId == windowId);
        if (filter.Outcome is { } outcome)
        {
            var statuses = EgressRunOutcomes.StatusesOf(outcome).ToArray();   // an array parameter translates to IN (...)
            query = query.Where(r => statuses.Contains(r.Status));
        }

        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.WindowId, r.Status, r.StartedByName, r.StartedAtUtc,
                WindowTitle = db.CheckingWindows.Where(w => w.Id == r.WindowId).Select(w => w.Title).FirstOrDefault() ?? string.Empty,
                Types = r.Outputs.OrderBy(o => o.OutputType).Select(o => o.OutputType).ToList(),
                Records = r.Outputs.Sum(o => o.OutputRecordCount ?? 0)
            })
            .ToListAsync(ct);

        return new EgressRunHistoryPage(
            rows.Select(r => new EgressRunHistoryRow(r.Id, r.WindowId, r.WindowTitle, r.Status, r.Types,
                r.Status == EgressRunStatus.Transferred ? r.Records : 0, r.StartedByName, r.StartedAtUtc)).ToList(),
            total, page, pageSize);
    }

    public async Task<bool> TrySetStatusAsync(Guid runId, EgressRunStatus from, EgressRunStatus to, CancellationToken ct) =>
        await db.EgressRuns.Where(r => r.Id == runId && r.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, to), ct) == 1;

    public Task<int> MarkPreprocessingFailedAsync(Guid runId, EgressRunStatus expectedStatus, IReadOnlyList<EgressRecordFailure> failures, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            var rows = await db.EgressRuns.Where(r => r.Id == runId && r.Status == expectedStatus).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, EgressRunStatus.PreprocessingFailed)
                .SetProperty(r => r.FailureJson, JsonSerializer.Serialize(failures, Json)), ct);
            if (rows > 0)
                await db.EgressRunOutputs.Where(o => o.RunId == runId).ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, false), ct);
            return rows;
        }, ct);

    public Task<int> SavePreprocessedAsync(Guid runId, EgressRunStatus expectedStatus, IReadOnlyList<NewLearnerRow> newLearners, IReadOnlyList<RemoveLearnerRow> removeLearners,
        DateOnly exportDate, IReadOnlyDictionary<EgressOutputType, string> fileNames, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            // S2: EnableRetryOnFailure re-runs this whole delegate on a transient fault. Without
            // clearing first, a prior attempt's AddRange calls stay tracked as Added (they were
            // never persisted, so ExecuteDeleteAsync below — a bulk operation that bypasses the
            // tracker — cannot remove them), and the retry's own AddRange would save both sets:
            // 2N learner rows for an N-row run, which is what ends up in the CSV.
            db.ChangeTracker.Clear();

            // M4: guard first — if the run moved on (e.g. Abandoned) while preprocessing ran,
            // nothing below must be written.
            var rows = await db.EgressRuns.Where(r => r.Id == runId && r.Status == expectedStatus).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, EgressRunStatus.Preprocessed)
                .SetProperty(r => r.PreprocessedAtUtc, DateTime.UtcNow)
                .SetProperty(r => r.ExportDate, exportDate)
                .SetProperty(r => r.FailureJson, (string?)null), ct);
            if (rows == 0) return 0;

            await db.EgressNewLearners.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
            await db.EgressRemoveLearners.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
            db.EgressNewLearners.AddRange(newLearners.Select(r => new EgressNewLearner
            {
                Id = Guid.NewGuid(), RunId = runId, ChangeRequestId = r.ChangeRequestId, TicketId = r.TicketId, ReferenceNumber = r.ReferenceNumber,
                CorrectionId = r.CorrectionId, CorrectionType = r.CorrectionType, KeyStage = r.KeyStage, LocalAuthority = r.LocalAuthority,
                EstablishmentNumber = r.EstablishmentNumber, Surname = r.Surname, Forename = r.Forename, Sex = r.Sex,
                DateOfBirth = r.DateOfBirth, AdmissionDate = r.AdmissionDate, Postcode = r.Postcode, CycleYear = r.CycleYear, CycleMonth = r.CycleMonth,
                SchoolUrn = r.SchoolUrn, Uln = r.Uln, Upn = r.Upn, LearnerId = r.LearnerId, YearGroup = r.YearGroup
            }));
            db.EgressRemoveLearners.AddRange(removeLearners.Select(r => new EgressRemoveLearner
            {
                Id = Guid.NewGuid(), RunId = runId, ChangeRequestId = r.ChangeRequestId, TicketId = r.TicketId, ReferenceNumber = r.ReferenceNumber,
                CorrectionId = r.CorrectionId, CorrectionType = r.CorrectionType, CorrectionReason = r.CorrectionReason, KeyStage = r.KeyStage,
                EstablishmentNumber = r.EstablishmentNumber, Surname = r.Surname, Forename = r.Forename, Sex = r.Sex, DateOfBirth = r.DateOfBirth,
                CycleYear = r.CycleYear, CycleMonth = r.CycleMonth, LocalAuthority = r.LocalAuthority, LearnerId = r.LearnerId,
                YearGroup = r.YearGroup, RemovalYear0 = r.RemovalYear0, RemovalYear1 = r.RemovalYear1, RemovalYear2 = r.RemovalYear2
            }));
            await db.SaveChangesAsync(ct);

            foreach (var (type, name) in fileNames)
            {
                var count = type == EgressOutputType.NewLearners ? newLearners.Count : removeLearners.Count;
                await db.EgressRunOutputs.Where(o => o.RunId == runId && o.OutputType == type)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.FileName, name).SetProperty(o => o.OutputRecordCount, count), ct);
            }
            return rows;
        }, ct);

    public async Task<IReadOnlyList<NewLearnerRow>> GetNewLearnersAsync(Guid runId, CancellationToken ct) =>
        await db.EgressNewLearners.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.Surname).ThenBy(x => x.Forename).ThenBy(x => x.ReferenceNumber)
            .Select(r => new NewLearnerRow(r.CorrectionId, r.CorrectionType, r.KeyStage, r.LocalAuthority, r.EstablishmentNumber, r.Surname,
                r.Forename, r.Sex, r.DateOfBirth, r.AdmissionDate, r.Postcode, r.CycleYear, r.CycleMonth, r.SchoolUrn, r.Uln, r.Upn, r.LearnerId,
                r.YearGroup, r.ChangeRequestId, r.TicketId, r.ReferenceNumber))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RemoveLearnerRow>> GetRemoveLearnersAsync(Guid runId, CancellationToken ct) =>
        await db.EgressRemoveLearners.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.Surname).ThenBy(x => x.Forename).ThenBy(x => x.ReferenceNumber)
            .Select(r => new RemoveLearnerRow(r.CorrectionId, r.CorrectionType, r.CorrectionReason, r.KeyStage, r.EstablishmentNumber, r.Surname, r.Forename,
                r.Sex, r.DateOfBirth, r.CycleYear, r.CycleMonth, r.LocalAuthority, r.LearnerId, r.ChangeRequestId, r.TicketId, r.ReferenceNumber)
            {
                YearGroup = r.YearGroup, RemovalYear0 = r.RemovalYear0, RemovalYear1 = r.RemovalYear1, RemovalYear2 = r.RemovalYear2
            })
            .ToListAsync(ct);

    public async Task<(EgressOutputType OutputType, EgressBlocker Blocker)?> TryReactivateAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            await db.EgressRunOutputs.Where(o => o.RunId == runId).ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, true), ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            return await BlockerForRunPairsAsync(runId, ct);
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return await BlockerForRunPairsAsync(runId, ct);
        }
    }

    private async Task<(EgressOutputType, EgressBlocker)?> BlockerForRunPairsAsync(Guid runId, CancellationToken ct)
    {
        var pairs = await db.EgressRunOutputs.AsNoTracking().Where(o => o.RunId == runId).Select(o => new { o.WindowId, o.OutputType }).ToListAsync(ct);
        foreach (var pair in pairs)
        {
            var blocker = await FindBlockerAsync(pair.WindowId, pair.OutputType, ct);
            if (blocker is not null && blocker.RunId != runId) return (pair.OutputType, blocker);
        }
        return null;
    }

    public Task<int> MarkTransferredAsync(Guid runId, EgressRunStatus expectedStatus, EgressTransferAudit audit, DateTime transferredAtUtc, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            // S2: same retry-duplication hazard as SavePreprocessedAsync — a stale tracked
            // AuditEntry from a prior attempt would otherwise be saved a second time alongside
            // this attempt's.
            db.ChangeTracker.Clear();

            // M4: guard first — if the run moved on (e.g. Abandoned mid-transfer) since the
            // Transferring flip, no file/output row is touched and no Succeeded audit is written.
            var rows = await db.EgressRuns.Where(r => r.Id == runId && r.Status == expectedStatus).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, EgressRunStatus.Transferred)
                .SetProperty(r => r.TransferredAtUtc, transferredAtUtc)
                .SetProperty(r => r.TransferredByName, audit.UserName)
                .SetProperty(r => r.TransferFailureReason, (string?)null), ct);
            if (rows == 0) return 0;

            foreach (var (type, file) in audit.Files)
            {
                await db.EgressRunOutputs.Where(o => o.RunId == runId && o.OutputType == type)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Sha256, file.Sha256).SetProperty(o => o.FileName, file.FileName), ct);
            }
            var run = await db.EgressRuns.AsNoTracking().FirstAsync(r => r.Id == runId, ct);
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = "EgressRun",
                EntityId = runId.ToString(),
                Action = "Transfer",
                Timestamp = transferredAtUtc,
                UserId = audit.UserId,
                NewValues = JsonSerializer.Serialize(new
                {
                    Outcome = "Succeeded",
                    run.WindowId,
                    OutputTypes = audit.Files.Keys.Select(k => k.ToString()).Order().ToList(),
                    Files = audit.Files.Select(f => new { OutputType = f.Key.ToString(), f.Value.FileName, f.Value.Records, f.Value.Sha256 }).ToList(),
                    audit.TargetContainer,
                    TransferredBy = audit.UserName,
                    TransferredAtUtc = transferredAtUtc
                }, Json)
            });
            await db.SaveChangesAsync(ct);
            return rows;
        }, ct);

    public Task<int> MarkTransferFailedAsync(Guid runId, EgressRunStatus expectedStatus, string reason, string userId, string userName, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            // S2: same retry-duplication hazard as SavePreprocessedAsync and MarkTransferredAsync
            // — a stale tracked AuditEntry from a prior attempt would otherwise be saved a second
            // time alongside this attempt's, leaving two TransferFailed audit rows for one failure.
            db.ChangeTracker.Clear();

            var clipped = reason.Length > 1000 ? reason[..1000] : reason;
            // M4: guard first — a run that has already moved on (e.g. Abandoned) must not be
            // overwritten to TransferFailed, and must not gain a spurious audit row for it.
            var rows = await db.EgressRuns.Where(r => r.Id == runId && r.Status == expectedStatus).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, EgressRunStatus.TransferFailed)
                .SetProperty(r => r.TransferFailureReason, clipped), ct);
            if (rows == 0) return 0;

            await db.EgressRunOutputs.Where(o => o.RunId == runId).ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, false), ct);

            // AB#294592: the audit row is the durable record of the attempt, so it names the window,
            // the output types and the person — the same fields the success row carries — rather
            // than leaving the audit log to join back to a run row that a dev reset may have wiped.
            var run = await db.EgressRuns.AsNoTracking().Include(r => r.Outputs).FirstAsync(r => r.Id == runId, ct);
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = "EgressRun", EntityId = runId.ToString(), Action = "TransferFailed",
                Timestamp = DateTime.UtcNow, UserId = userId,
                NewValues = JsonSerializer.Serialize(new
                {
                    Outcome = "Failed",
                    run.WindowId,
                    OutputTypes = run.Outputs.Select(o => o.OutputType.ToString()).Order().ToList(),
                    TransferredBy = userName,
                    Reason = clipped
                }, Json)
            });
            await db.SaveChangesAsync(ct);
            return rows;
        }, ct);

    public Task<int> AbandonAsync(Guid runId, CancellationToken ct) =>
        db.ExecuteInTransactionAsync(async () =>
        {
            // Excludes only Transferred — Preprocessing and Transferring are both admitted, so a
            // run stuck there (a pod restart mid-pipeline) can always be released (M4).
            var rows = await db.EgressRuns.Where(r => r.Id == runId && r.Status != EgressRunStatus.Transferred)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, EgressRunStatus.Abandoned), ct);
            if (rows > 0)
                await db.EgressRunOutputs.Where(o => o.RunId == runId).ExecuteUpdateAsync(s => s.SetProperty(o => o.IsActive, false), ct);
            return rows;
        }, ct);
}
