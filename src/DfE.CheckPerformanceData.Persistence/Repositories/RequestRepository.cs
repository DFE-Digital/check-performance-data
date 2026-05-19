using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RequestRepository(IPortalDbContext db) : IRequestRepository
{
    public async Task SaveAsync(RequestDocument document)
    {
        var request = new ChangeRequest
        {
            Id = Guid.NewGuid(),
            WindowId = document.CheckingWindowId,
            OrganisationUrn = long.Parse(document.School.Urn),
            PupilUpn = document.Pupil.Upn,
            PupilFirstname = document.Pupil.Firstname,
            PupilSurname = document.Pupil.Surname,
            Submitted = DateTime.SpecifyKind(document.SubmittedAt, DateTimeKind.Unspecified),
            SubmittedById = Guid.Parse(document.SubmittedBy.UserId),
            SubmittedByName = document.SubmittedBy.DisplayName,
            Status = RequestStatus.Submitted,
            ReferenceNumber = document.ReferenceNumber
        };

        await db.ChangeRequests.AddAsync(request);
        await db.SaveChangesAsync();
    }
}
