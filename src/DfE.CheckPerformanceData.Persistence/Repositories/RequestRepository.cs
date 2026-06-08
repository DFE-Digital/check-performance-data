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

    public async Task UpsertAsync(ChangeRequestData data)
    {
        var timestamp = DateTime.SpecifyKind(data.Timestamp, DateTimeKind.Unspecified);

        var updated = await db.ChangeRequests
            .Where(r => r.ReferenceNumber == data.ReferenceNumber)
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

        if (updated == 0)
        {
            await db.ChangeRequests.AddAsync(new ChangeRequest
            {
                Id = Guid.NewGuid(),
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
        }
    }
}
