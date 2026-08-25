using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RequestRepository(IPortalDbContext db) : IRequestRepository
{
    public async Task<DuplicateCheckResult> CheckForConflictAsync(Guid windowId, Guid pupilId, long organisationUrn, string currentReferenceNumber, Guid currentUserId)
    {
        var conflict = await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.PupilId == pupilId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber != currentReferenceNumber
                && r.Status == RequestStatus.SubmittedUnCommitted
                // AB#296648: a results enquiry is not a competing amendment, so it must not raise the
                // pupil-search duplicate warning. Kept in step with ConflictQuery below — the warning
                // and the hard block have to agree, or the user is warned about something that then
                // submits fine (or worse, the reverse).
                && r.RequestType != RequestType.ResultsEnquiry)
            .OrderByDescending(r => r.Submitted)
            .Select(r => new { r.SubmittedById, r.ReferenceNumber, r.RequestTypeDescription, r.SubmittedByName })
            .FirstOrDefaultAsync();

        if (conflict is null)
            return new DuplicateCheckResult.NoConflict();

        var conflictingReasonType = ExtractConflictingReasonType(conflict.RequestTypeDescription);
        var conflictingCategory = ExtractRequestCategory(conflict.RequestTypeDescription);

        return conflict.SubmittedById == currentUserId
            ? new DuplicateCheckResult.SelfSubmitted(conflict.ReferenceNumber, conflictingReasonType, conflictingCategory, conflict.SubmittedByName ?? string.Empty)
            : new DuplicateCheckResult.OtherSubmitted(conflict.ReferenceNumber, conflictingReasonType, conflictingCategory, conflict.SubmittedByName ?? string.Empty);
    }

    public async Task<string?> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn)
        => await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.PupilId == pupilId
                && r.OrganisationUrn == organisationUrn
                && r.Status == RequestStatus.SubmittedUnCommitted)
            .Select(r => r.ReferenceNumber)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Guid>> GetSubmittedPupilIdsAsync(Guid windowId, long organisationUrn) 
        => await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.Status == RequestStatus.SubmittedUnCommitted
                && r.PupilId != null)
            .Select(r => r.PupilId!.Value)
            .Distinct()
            .ToListAsync();
    
    public async Task<Guid> UpsertAsync(ChangeRequestData data)
    {
        var timestamp = DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Local);

        // For SubmittedUnCommitted, check for conflicts atomically within a serializable
        // transaction covering both insert and update paths. This closes the TOCTOU gap
        // between CheckForConflictAsync and UpsertAsync — two concurrent submissions for
        // the same pupil cannot both succeed.
        if (data.Status == RequestStatus.SubmittedUnCommitted)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                // ReferenceNumber is unique, so at most one row matches.
                // Lookup is inside the serializable transaction to close the TOCTOU gap
                // between the lookup and the write — a concurrent transaction that inserts
                // a row with the same ReferenceNumber will be visible on retry.
                var existingId = await db.ChangeRequests
                    .Where(r => r.ReferenceNumber == data.ReferenceNumber)
                    .Select(r => r.Id)
                    .FirstOrDefaultAsync();

                // AB#296648: the duplicate rule guards against two competing AMENDMENTS to the same
                // pupil's record — only one can be right, so the second is a conflict to resolve. A
                // results enquiry is not an amendment: it changes no pupil data, and the spec
                // explicitly allows several for the same pupil and result. So an enquiry neither
                // raises a conflict nor counts as one. Without both exclusions, reporting a wrong
                // grade for a pupil would block every later amendment for them (and vice versa), with
                // an error message naming an unrelated request.
                var conflict = data.RequestType == RequestType.ResultsEnquiry
                    ? null
                    : await ConflictQuery(db, data, existingId).FirstOrDefaultAsync();

                if (conflict is not null)
                {
                    var conflictingReasonType = ExtractConflictingReasonType(conflict.RequestTypeDescription);
                    var conflictingCategory = ExtractRequestCategory(conflict.RequestTypeDescription);
                    var reasonsMatch = conflictingReasonType.Equals(
                        ExtractConflictingReasonType(data.RequestTypeDescription), StringComparison.OrdinalIgnoreCase);
                    throw new DuplicateRequestException(
                        conflict.SubmittedById == data.SubmittedById
                            ? ConflictType.SelfSubmitted
                            : ConflictType.OtherSubmitted,
                        conflictingReasonType, conflictingCategory, conflict.SubmittedByName ?? string.Empty, reasonsMatch);
                }

                Guid id;
                if (existingId != Guid.Empty)
                {
                    id = existingId;
                    await db.ChangeRequests
                        .Where(r => r.Id == existingId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.Status, data.Status)
                            .SetProperty(r => r.Submitted, timestamp)
                            .SetProperty(r => r.OrganisationUrn, data.OrganisationUrn)
                            .SetProperty(r => r.PupilId, data.PupilId)
                            .SetProperty(r => r.PupilUpn, data.PupilUpn)
                            .SetProperty(r => r.PupilFirstname, data.PupilFirstname)
                            .SetProperty(r => r.PupilSurname, data.PupilSurname)
                            .SetProperty(r => r.SubmittedById, data.SubmittedById)
                            .SetProperty(r => r.SubmittedByName, data.SubmittedByName)
                            .SetProperty(r => r.SubmittedByEmail, data.SubmittedByEmail)
                            .SetProperty(r => r.RequestType, data.RequestType)
                            .SetProperty(r => r.RequestTypeDescription, data.RequestTypeDescription)
                            .SetProperty(r => r.AmendmentType, data.AmendmentType));
                }
                else
                {
                    id = Guid.NewGuid();
                    db.ChangeRequests.Add(new ChangeRequest
                    {
                        Id = id,
                        WindowId = data.WindowId,
                        ReferenceNumber = data.ReferenceNumber,
                        OrganisationUrn = data.OrganisationUrn,
                        PupilId = data.PupilId,
                        PupilUpn = data.PupilUpn,
                        PupilFirstname = data.PupilFirstname,
                        PupilSurname = data.PupilSurname,
                        Submitted = timestamp,
                        SubmittedById = data.SubmittedById,
                        SubmittedByName = data.SubmittedByName,
                        SubmittedByEmail = data.SubmittedByEmail,
                        Status = data.Status,
                        RequestType = data.RequestType,
                        RequestTypeDescription = data.RequestTypeDescription,
                        AmendmentType = data.AmendmentType
                    });
                    await db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                return id;
            });
        }

        // Draft save path (non-SubmittedUnCommitted) — no conflict guard needed.
        var draftExistingId = await db.ChangeRequests
            .Where(r => r.ReferenceNumber == data.ReferenceNumber)
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

        if (draftExistingId != Guid.Empty)
        {
            await db.ChangeRequests
                .Where(r => r.Id == draftExistingId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, data.Status)
                    .SetProperty(r => r.Submitted, timestamp)
                    .SetProperty(r => r.OrganisationUrn, data.OrganisationUrn)
                    .SetProperty(r => r.PupilId, data.PupilId)
                    .SetProperty(r => r.PupilUpn, data.PupilUpn)
                    .SetProperty(r => r.PupilFirstname, data.PupilFirstname)
                    .SetProperty(r => r.PupilSurname, data.PupilSurname)
                    .SetProperty(r => r.SubmittedById, data.SubmittedById)
                    .SetProperty(r => r.SubmittedByName, data.SubmittedByName)
                    .SetProperty(r => r.SubmittedByEmail, data.SubmittedByEmail)
                    .SetProperty(r => r.RequestType, data.RequestType)
                    .SetProperty(r => r.RequestTypeDescription, data.RequestTypeDescription)
                    .SetProperty(r => r.AmendmentType, data.AmendmentType));
            return draftExistingId;
        }

        var newId = Guid.NewGuid();
        await db.ChangeRequests.AddAsync(new ChangeRequest
        {
            Id = newId,
            WindowId = data.WindowId,
            ReferenceNumber = data.ReferenceNumber,
            OrganisationUrn = data.OrganisationUrn,
            PupilId = data.PupilId,
            PupilUpn = data.PupilUpn,
            PupilFirstname = data.PupilFirstname,
            PupilSurname = data.PupilSurname,
            Submitted = timestamp,
            SubmittedById = data.SubmittedById,
            SubmittedByName = data.SubmittedByName,
            SubmittedByEmail = data.SubmittedByEmail,
            Status = data.Status,
            RequestType = data.RequestType,
            RequestTypeDescription = data.RequestTypeDescription,
            AmendmentType = data.AmendmentType
        });
        await db.SaveChangesAsync();
        return newId;
    }

    public async Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(Guid windowId, long organisationUrn) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && (r.Status == RequestStatus.InProgress || r.Status == RequestStatus.ReadyToSubmit))
            .OrderBy(r => r.PupilSurname)
            .ThenBy(r => r.PupilFirstname)
            .Select(r => new AmendmentRequestData
            {
                PupilId = r.PupilId,
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
            })
            .ToListAsync();

    public async Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedRequestsAsync(Guid windowId, long organisationUrn) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && (r.Status == RequestStatus.SubmittedUnCommitted
                    || r.Status == RequestStatus.Withdrawn)
                // AB#296648 — how the Amendment Requests screen should present a results enquiry
                // is not yet designed, so enquiry rows are hidden here for now; they remain in
                // ChangeRequests and reach support via the separate Zendesk story.
                && r.RequestType != RequestType.ResultsEnquiry)
            .OrderByDescending(r => r.Submitted)
            .Select(r => new SubmittedRequestData
            {
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted
            })
            .ToListAsync();

    public async Task<IReadOnlyList<SubmittedRequestData>> GetAllSubmittedRequestsAsync(
        long organisationUrn) =>
        await db.ChangeRequests
            .Where(r => r.OrganisationUrn == organisationUrn)
            .OrderByDescending(r => r.Submitted)
            .Select(r => new SubmittedRequestData
            {
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted,
                WindowId = r.WindowId
            })
            .ToListAsync();

    public async Task<AmendmentRequestData?> GetAmendmentRequestAsync(
        Guid windowId, long organisationUrn, string referenceNumber) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .Select(r => new AmendmentRequestData
            {
                PupilId = r.PupilId,
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber,
                SubmittedByEmail = r.SubmittedByEmail,
                Submitted = r.Submitted,
                WithdrawnByEmail = r.WithdrawnByEmail,
                WithdrawnAt = r.WithdrawnAt
            })
            .FirstOrDefaultAsync();

    public Task WithdrawAsync(Guid windowId, long organisationUrn, string referenceNumber, string withdrawnByEmail, DateTime withdrawnAt) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RequestStatus.Withdrawn)
                .SetProperty(r => r.WithdrawnByEmail, withdrawnByEmail)
                .SetProperty(r => r.WithdrawnAt, withdrawnAt));

    public Task DeleteAsync(Guid windowId, long organisationUrn, string referenceNumber) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .ExecuteDeleteAsync();

    public async Task<ConfirmDataCorrectData?> GetConfirmDataCorrectAsync(Guid windowId, long organisationUrn, string referenceNumber) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .Select(r => new ConfirmDataCorrectData
            {
                RequestType = r.RequestType,
                Status = r.Status,
                SubmittedByEmail = r.SubmittedByEmail,
                Submitted = r.Submitted,
                WithdrawnByEmail = r.WithdrawnByEmail,
                WithdrawnAt = r.WithdrawnAt,
                ReferenceNumber = r.ReferenceNumber
            })
            .FirstOrDefaultAsync();

    /// <summary>
    /// Rows that would conflict with <paramref name="data"/>: another submitted-uncommitted amendment
    /// for the same pupil, at the same school, in the same window.
    ///
    /// Results enquiries are excluded (AB#296648) — they change no pupil data, so they neither
    /// compete with an amendment nor with each other.
    /// </summary>
    private static IQueryable<ChangeRequest> ConflictQuery(IPortalDbContext db, ChangeRequestData data, Guid existingId)
    {
        var query = db.ChangeRequests
            .Where(r => r.WindowId == data.WindowId
                        && r.PupilId == data.PupilId
                        && r.OrganisationUrn == data.OrganisationUrn
                        && r.Status == RequestStatus.SubmittedUnCommitted
                        && r.RequestType != RequestType.ResultsEnquiry);

        // When updating an existing row, exclude the current row from conflict
        // detection so a user can re-submit or update their own draft.
        return existingId != Guid.Empty ? query.Where(r => r.Id != existingId) : query;
    }

    private static string ExtractConflictingReasonType(string requestTypeDescription)
    {
        var separatorIndex = requestTypeDescription.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0
            ? requestTypeDescription[(separatorIndex + 3)..]
            : requestTypeDescription;
    }

    private static string ExtractRequestCategory(string requestTypeDescription)
    {
        var separatorIndex = requestTypeDescription.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0
            ? requestTypeDescription[..separatorIndex]
            : requestTypeDescription;
    }
}
