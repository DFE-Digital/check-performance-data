using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.AmendmentRequests;

// The admin requests page is scoped to one checking window and filterable by that window's
// exercises. Both filters are real SQL, so they are pinned against a real database: the exercise
// filter in particular resolves a type to THIS window's exercise row, and a type comparison that
// leaked across windows would silently show another window's requests.
[Collection(nameof(PostgresCollection))]
public sealed class AdminRequestsRepositoryWindowFilterTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task GetForWindowAsync_ReturnsOnlyTheRequestedWindowsRows()
    {
        await TruncateAsync();
        var a = await SeedWindowAsync("Window A");
        var b = await SeedWindowAsync("Window B");
        await SeedRequestAsync(a.WindowId, "REF-A", a.PupilDataId);
        await SeedRequestAsync(b.WindowId, "REF-B", b.PupilDataId);

        var rows = await Repository().GetForWindowAsync(a.WindowId, null, CancellationToken.None);

        Assert.Equal("REF-A", Assert.Single(rows).ReferenceNumber);
    }

    [Fact]
    public async Task GetForWindowAsync_WithNoFilter_ReturnsEveryExercisesRows()
    {
        await TruncateAsync();
        var w = await SeedWindowAsync("Window A");
        await SeedRequestAsync(w.WindowId, "REF-PUPIL", w.PupilDataId);
        await SeedRequestAsync(w.WindowId, "REF-ENQUIRY", w.ResultsEnquiryId);

        var rows = await Repository().GetForWindowAsync(w.WindowId, null, CancellationToken.None);

        Assert.Equal(2, rows.Count);
    }

    [Theory]
    [InlineData(CheckingExerciseType.PupilData, "REF-PUPIL")]
    [InlineData(CheckingExerciseType.ResultsEnquiry, "REF-ENQUIRY")]
    public async Task GetForWindowAsync_FiltersToTheNamedExercise(
        CheckingExerciseType exercise, string expectedReference)
    {
        await TruncateAsync();
        var w = await SeedWindowAsync("Window A");
        await SeedRequestAsync(w.WindowId, "REF-PUPIL", w.PupilDataId);
        await SeedRequestAsync(w.WindowId, "REF-ENQUIRY", w.ResultsEnquiryId);

        var rows = await Repository().GetForWindowAsync(w.WindowId, exercise, CancellationToken.None);

        Assert.Equal(expectedReference, Assert.Single(rows).ReferenceNumber);
    }

    [Fact]
    public async Task GetForWindowAsync_FilteringExcludesAnUnstampedRow()
    {
        // A row with no CheckingExerciseId cannot claim to belong to the exercise being asked
        // about, so a filter must drop it — while the unfiltered view still shows it.
        await TruncateAsync();
        var w = await SeedWindowAsync("Window A");
        await SeedRequestAsync(w.WindowId, "REF-UNSTAMPED", exerciseId: null);

        Assert.Single(await Repository().GetForWindowAsync(w.WindowId, null, CancellationToken.None));
        Assert.Empty(await Repository()
            .GetForWindowAsync(w.WindowId, CheckingExerciseType.PupilData, CancellationToken.None));
    }

    [Fact]
    public async Task GetForWindowAsync_FilteringByAnExerciseThisWindowDoesNotRunReturnsNothing()
    {
        // Fails closed: no exercise row means no id to match, and the alternative — treating the
        // missing id as "no filter" — would show every request under a heading naming an exercise
        // the window never ran. The service drops such a filter before it reaches here; this pins
        // the repository's own behaviour so the two cannot disagree.
        await TruncateAsync();
        var w = await SeedWindowAsync("Window A", withResultsEnquiry: false);
        await SeedRequestAsync(w.WindowId, "REF-PUPIL", w.PupilDataId);

        var rows = await Repository()
            .GetForWindowAsync(w.WindowId, CheckingExerciseType.ResultsEnquiry, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task GetForWindowAsync_ProjectsEachRowsExerciseAndLeavesAnUnstampedRowNull()
    {
        // The Exercise column on the page is read from the row's own CheckingExerciseId, never
        // from the filter — the unfiltered view mixes both exercises in one table.
        await TruncateAsync();
        var w = await SeedWindowAsync("Window A");
        await SeedRequestAsync(w.WindowId, "REF-PUPIL", w.PupilDataId);
        await SeedRequestAsync(w.WindowId, "REF-ENQUIRY", w.ResultsEnquiryId);
        await SeedRequestAsync(w.WindowId, "REF-UNSTAMPED", exerciseId: null);

        var rows = await Repository().GetForWindowAsync(w.WindowId, null, CancellationToken.None);
        var byReference = rows.ToDictionary(r => r.ReferenceNumber, r => r.Exercise);

        Assert.Equal(CheckingExerciseType.PupilData, byReference["REF-PUPIL"]);
        Assert.Equal(CheckingExerciseType.ResultsEnquiry, byReference["REF-ENQUIRY"]);
        Assert.Null(byReference["REF-UNSTAMPED"]);
    }

    private AdminRequestsRepository Repository() => new(_fixture.CreateContext());

    private async Task<(Guid WindowId, Guid PupilDataId, Guid? ResultsEnquiryId)> SeedWindowAsync(
        string title, bool withResultsEnquiry = true)
    {
        await using var ctx = _fixture.CreateContext();
        var window = new CheckingWindow
        {
            Id = Guid.NewGuid(),
            Title = title,
            KeyStage = KeyStages.Post16,
            CheckingWindowType = CheckingWindowType.Post16,
            StartDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(20), DateTimeKind.Unspecified)
        };

        var pupilData = new CheckingExercise
        {
            Id = Guid.NewGuid(),
            ExerciseType = CheckingExerciseType.PupilData,
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            SortOrder = 0
        };
        window.CheckingExercises.Add(pupilData);

        CheckingExercise? enquiry = null;
        if (withResultsEnquiry)
        {
            enquiry = new CheckingExercise
            {
                Id = Guid.NewGuid(),
                ExerciseType = CheckingExerciseType.ResultsEnquiry,
                StartDate = window.StartDate,
                EndDate = window.EndDate,
                SortOrder = 1
            };
            window.CheckingExercises.Add(enquiry);
        }

        ctx.CheckingWindows.Add(window);
        await ctx.SaveChangesAsync();
        return (window.Id, pupilData.Id, enquiry?.Id);
    }

    private Task SeedRequestAsync(Guid windowId, string reference, Guid? exerciseId) =>
        new RequestRepository(_fixture.CreateContext()).UpsertAsync(new ChangeRequestData
        {
            WindowId = windowId,
            CheckingExerciseId = exerciseId,
            ReferenceNumber = reference,
            OrganisationUrn = 100000,
            PupilId = Guid.NewGuid(),
            PupilFirstname = "Jane",
            PupilSurname = "Smith",
            Timestamp = DateTime.UtcNow,
            SubmittedById = Guid.NewGuid(),
            SubmittedByName = "Test User",
            Status = RequestStatus.SubmittedUnCommitted,
            RequestType = RequestType.Amendment,
            RequestTypeDescription = "Remove"
        });

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ChangeRequests"" CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }
}
