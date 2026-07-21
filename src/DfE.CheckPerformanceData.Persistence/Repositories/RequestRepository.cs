using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RequestRepository(IPortalDbContext db) : IRequestRepository
{
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

    public async Task<DuplicateCheckResult> CheckForConflictAsync(
        Guid windowId, Guid pupilId, long organisationUrn, string currentReferenceNumber, Guid currentUserId)
    {
        var conflict = await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.PupilId == pupilId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber != currentReferenceNumber
                && r.Status == RequestStatus.SubmittedUnCommitted)
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

    public async Task<string?> HasSubmittedRequestAsync(
        Guid windowId, Guid pupilId, long organisationUrn) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.PupilId == pupilId
                && r.OrganisationUrn == organisationUrn
                && r.Status == RequestStatus.SubmittedUnCommitted)
            .Select(r => r.ReferenceNumber)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<Guid>> GetSubmittedPupilIdsAsync(
        Guid windowId, long organisationUrn) =>
        await db.ChangeRequests
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

        // ReferenceNumber is unique, so at most one row matches.
        var existingId = await db.ChangeRequests
            .Where(r => r.ReferenceNumber == data.ReferenceNumber)
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

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

                var conflictQuery = db.ChangeRequests
                    .Where(r => r.WindowId == data.WindowId
                        && r.PupilId == data.PupilId
                        && r.OrganisationUrn == data.OrganisationUrn
                        && r.Status == RequestStatus.SubmittedUnCommitted);

                // When updating an existing row, exclude the current row from conflict
                // detection so a user can re-submit or update their own draft.
                if (existingId != Guid.Empty)
                    conflictQuery = conflictQuery.Where(r => r.Id != existingId);

                var conflict = await conflictQuery.FirstOrDefaultAsync();

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
                            .SetProperty(r => r.RequestTypeDescription, data.RequestTypeDescription));
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
                        RequestTypeDescription = data.RequestTypeDescription
                    });
                    await db.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                return id;
            });
        }

        // Draft save path (non-SubmittedUnCommitted) — no conflict guard needed.
        if (existingId != Guid.Empty)
        {
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
                    .SetProperty(r => r.RequestTypeDescription, data.RequestTypeDescription));
            return existingId;
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
            RequestTypeDescription = data.RequestTypeDescription
        });
        await db.SaveChangesAsync();
        return newId;
    }

    public async Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(
        Guid windowId, long organisationUrn) =>
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

    public async Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedRequestsAsync(
        Guid windowId, long organisationUrn) =>
        await db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && (r.Status == RequestStatus.SubmittedUnCommitted
                    || r.Status == RequestStatus.Withdrawn))
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
                Submitted = r.Submitted
            })
            .FirstOrDefaultAsync();

    public Task WithdrawAsync(Guid windowId, long organisationUrn, string referenceNumber) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Withdrawn));

    public Task DeleteAsync(Guid windowId, long organisationUrn, string referenceNumber) =>
        db.ChangeRequests
            .Where(r => r.WindowId == windowId
                && r.OrganisationUrn == organisationUrn
                && r.ReferenceNumber == referenceNumber)
            .ExecuteDeleteAsync();

    public async Task<ConfirmDataCorrectData?> GetConfirmDataCorrectAsync(
        Guid windowId, long organisationUrn, string referenceNumber) =>
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
                ReferenceNumber = r.ReferenceNumber
            })
            .FirstOrDefaultAsync();
}
