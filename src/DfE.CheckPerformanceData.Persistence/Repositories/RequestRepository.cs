using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RequestRepository(IPortalDbContext db) : IRequestRepository
{
    public Task<bool> HasConflictingRequestAsync(
        Guid windowId, string pupilUpn, long organisationUrn, string currentReferenceNumber) =>
        db.ChangeRequests.AnyAsync(r =>
            r.WindowId == windowId &&
            r.PupilUpn == pupilUpn &&
            r.OrganisationUrn == organisationUrn &&
            r.ReferenceNumber != currentReferenceNumber);

    public async Task<Guid> UpsertAsync(ChangeRequestData data)
    {
        var timestamp = DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Unspecified);

        // ReferenceNumber is unique, so at most one row matches.
        var existingId = await db.ChangeRequests
            .Where(r => r.ReferenceNumber == data.ReferenceNumber)
            .Select(r => r.Id)
            .FirstOrDefaultAsync();

        if (existingId != Guid.Empty)
        {
            await db.ChangeRequests
                .Where(r => r.Id == existingId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, data.Status)
                    .SetProperty(r => r.Submitted, timestamp)
                    .SetProperty(r => r.OrganisationUrn, data.OrganisationUrn)
                    .SetProperty(r => r.PupilUpn, data.PupilUpn)
                    .SetProperty(r => r.PupilFirstname, data.PupilFirstname)
                    .SetProperty(r => r.PupilSurname, data.PupilSurname)
                    .SetProperty(r => r.SubmittedById, data.SubmittedById)
                    .SetProperty(r => r.SubmittedByName, data.SubmittedByName)
                    .SetProperty(r => r.RequestType, data.RequestType));
            return existingId;
        }

        var id = Guid.NewGuid();
        await db.ChangeRequests.AddAsync(new ChangeRequest
        {
            Id = id,
            WindowId = data.WindowId,
            ReferenceNumber = data.ReferenceNumber,
            OrganisationUrn = data.OrganisationUrn,
            PupilUpn = data.PupilUpn,
            PupilFirstname = data.PupilFirstname,
            PupilSurname = data.PupilSurname,
            Submitted = timestamp,
            SubmittedById = data.SubmittedById,
            SubmittedByName = data.SubmittedByName,
            Status = data.Status,
            RequestType = data.RequestType
        });
        await db.SaveChangesAsync();
        return id;
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
                PupilFirstname = r.PupilFirstname,
                PupilSurname = r.PupilSurname,
                RequestType = r.RequestType,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
            })
            .ToListAsync();
}
