using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.ResultsEnquiry;

[Collection(nameof(PostgresCollection))]
public sealed class DuplicateEnquiryRepositoryTests(PostgresFixture fixture)
{
    private const long School = 100000;
    private const long OtherSchool = 999999;
    private static readonly Guid Student = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly EnquirySubject Sport2024 = Result("60172186", "S2024");
    private static readonly EnquirySubject MissingSport = EnquirySubject.ForQualification(
        new QualificationReference { Qan = "60172186", QualificationTitle = "A made-up qualification" });

    private static EnquirySubject Result(string qan, string session) =>
        EnquirySubject.ForResult(new ResultRecord { Qan = qan, Session = session });

    private RequestRepository Repository() => new(fixture.CreateContext());

    private Task<string?> FindAsync(Guid windowId, EnquirySubject subject, long school = School, Guid? student = null,
        string? excluded = null) =>
        Repository().FindSubmittedResultsEnquiryReferenceAsync(windowId, school, student ?? Student, subject, excluded);

    [Fact]
    public async Task Finds_the_submitted_enquiry_for_the_same_student_and_subject()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        Assert.Equal("REF-1", await FindAsync(windowId, Sport2024));
    }

    [Fact]
    public async Task Finds_nothing_for_another_student_qualification_session_school_or_window()
    {
        var windowId = await StartAsync();
        var otherWindowId = await SeedWindowAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        Assert.Null(await FindAsync(windowId, Sport2024, student: Guid.NewGuid()));
        Assert.Null(await FindAsync(windowId, Result("60146084", "S2024")));
        Assert.Null(await FindAsync(windowId, Result("60172186", "S2023")));
        Assert.Null(await FindAsync(windowId, Sport2024, school: OtherSchool));
        Assert.Null(await FindAsync(otherWindowId, Sport2024));
    }

    [Fact]
    public async Task A_missing_qualification_matches_only_an_enquiry_with_no_session()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-RESULT", Sport2024));

        Assert.Null(await FindAsync(windowId, MissingSport));

        await Repository().UpsertAsync(Enquiry(windowId, "REF-MISSING", MissingSport));

        Assert.Equal("REF-MISSING", await FindAsync(windowId, MissingSport));
        Assert.Equal("REF-RESULT", await FindAsync(windowId, Sport2024));
    }

    [Fact]
    public async Task A_withdrawn_enquiry_is_not_found()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));
        await Repository().WithdrawAsync(windowId, School, "REF-1", "ada@school.test", DateTime.UtcNow);

        Assert.Null(await FindAsync(windowId, Sport2024));
    }

    [Fact]
    public async Task The_callers_own_request_and_a_row_with_no_subject_are_not_found()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));
        await Repository().UpsertAsync(Enquiry(windowId, "REF-OLD", subject: null));
        await Repository().UpsertAsync(Enquiry(windowId, "REF-AMD", subject: null, requestType: RequestType.Amendment));

        Assert.Null(await FindAsync(windowId, Sport2024, excluded: "REF-1"));
    }

    [Fact]
    public async Task Submitting_a_second_enquiry_for_the_same_student_and_subject_is_refused()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        var refused = await Assert.ThrowsAsync<DuplicateEnquiryException>(
            () => Repository().UpsertAsync(Enquiry(windowId, "REF-2", Sport2024)));

        Assert.Equal("REF-1", refused.ExistingReferenceNumber);
        Assert.Equal(1, await CountAsync(windowId));
    }

    [Fact]
    public async Task Submitting_is_allowed_for_another_subject_another_student_and_after_a_withdrawal()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        await Repository().UpsertAsync(Enquiry(windowId, "REF-2", Result("60172186", "S2023")));
        await Repository().UpsertAsync(Enquiry(windowId, "REF-3", Sport2024, student: Guid.NewGuid()));
        await Repository().UpsertAsync(Enquiry(windowId, "REF-4", MissingSport));
        await Repository().WithdrawAsync(windowId, School, "REF-1", "ada@school.test", DateTime.UtcNow);
        await Repository().UpsertAsync(Enquiry(windowId, "REF-5", Sport2024));

        Assert.Equal(5, await CountAsync(windowId));
    }

    [Fact]
    public async Task Submitting_the_same_reference_again_is_not_a_duplicate_of_itself()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        Assert.Equal(1, await CountAsync(windowId));
    }

    [Fact]
    public async Task An_amendment_for_the_student_does_not_block_an_enquiry_and_an_enquiry_does_not_block_an_amendment()
    {
        var windowId = await StartAsync();
        await Repository().UpsertAsync(Enquiry(windowId, "REF-AMD-1", subject: null, requestType: RequestType.Amendment));
        await Repository().UpsertAsync(Enquiry(windowId, "REF-1", Sport2024));

        Assert.Equal(2, await CountAsync(windowId));
    }

    private static ChangeRequestData Enquiry(
        Guid windowId, string referenceNumber, EnquirySubject? subject, Guid? student = null,
        RequestType requestType = RequestType.ResultsEnquiry) =>
        new()
        {
            WindowId = windowId,
            ReferenceNumber = referenceNumber,
            OrganisationUrn = School,
            PupilId = student ?? Student,
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.Submitted,
            RequestType = requestType,
            RequestTypeDescription = requestType.ToString(),
            EnquirySubject = subject
        };

    private async Task<Guid> StartAsync()
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
        return await SeedWindowAsync();
    }

    private async Task<int> CountAsync(Guid windowId)
    {
        await using var ctx = fixture.CreateContext();
        return ctx.ChangeRequests.Count(r => r.WindowId == windowId);
    }

    private async Task<Guid> SeedWindowAsync()
    {
        await using var ctx = fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = "16 to 19",
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };
        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return window.Id;
    }
}
