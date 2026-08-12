using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Seeding;

namespace DfE.CheckPerformanceData.Web.Seeding;

// Dev-only: seeds ChangeRequests rows AND their RequestState blobs for Kingsmead School so a
// developer can exercise the Amendment requests screen and the bulk submission / validation
// flow (ticking multiple ReadyToSubmit drafts, editing an InProgress draft, hitting the
// already-submitted/duplicate-pupil warnings). Runs after SeedPupilData, which is where the
// pupils referenced here come from.
//
// Two windows are seeded: the open KS4 June window (the in-flight scenarios above) and the
// closed KS4 June window from last year, with half as many rows, so the establishment-wide
// Amendment requests grid has both an editable and a read-only (window closed) window in it.
public static class SeedChangeRequests
{
    private const string Laestab = "860/4070"; // Kingsmead School
    private const long Urn = 142313;

    // Fixed dev "submitter" identity so seeded rows are stable across re-seeds.
    private static readonly Guid SubmittedById = Guid.Parse("00000000-0000-0000-0000-0000000000AA");
    private const string SubmittedByName = "Dev Seed";
    private const string SubmittedByEmail = "dev-seed@example.com";

    // "Dual registered or moved school": its branch page (dual-registered-moved) has no
    // page-level nextPageId, so it goes straight to the Summary/end without an evidence
    // upload page — the simplest complete, coherent Remove journey to seed.
    private const string ReasonValue = "dual-registered-moved";
    private const string ReasonLabel = "Dual registered or moved school";
    private const string ReasonDfeNumber = "123/4567";

    // Matches what the real submission path produces ("{WhatToChange} - {reason label}") so the
    // seeded rows read identically to genuine requests in the Amendment requests / bulk grids.
    private const string RequestTypeDescription = "Remove - " + ReasonLabel;

    private readonly record struct Scenario(string Reference, RequestStatus Status, IPupilRecord Pupil);

    public static async Task ExecuteSeedAsync(
        IPupilDataBlobClient pupilClient,
        IRequestRepository requestRepository,
        IRequestStateBlobClient requestStateBlobClient,
        ICheckYourPupilDataService checkYourPupilDataService)
    {
        await SeedOpenWindowAsync(pupilClient, requestRepository, requestStateBlobClient, checkYourPupilDataService);
        await SeedClosedWindowAsync(pupilClient, requestRepository, requestStateBlobClient, checkYourPupilDataService);
    }

    // The live KS4 June window: in-flight statuses, duplicate pairs and drafts, so the bulk
    // submission and validation journeys can all be exercised.
    private static async Task SeedOpenWindowAsync(
        IPupilDataBlobClient pupilClient,
        IRequestRepository requestRepository,
        IRequestStateBlobClient requestStateBlobClient,
        ICheckYourPupilDataService checkYourPupilDataService)
    {
        var windowId = DevDataSeeder.KeyStage4JuneCheckingWindowId;

        // Seeded change requests are KS4-only; the window above is the KS4 June dev window.
        var pupils = await pupilClient.GetPupilsAsync(
            windowId, CheckingExerciseType.PupilData, Laestab, CheckingWindowType.KS4June);
        if (pupils is null || pupils.Count == 0) return;
        var p = await GetIncludedPupilsAsync(pupilClient, windowId, required: 9);
        if (p is null) return;

        var scenarios = new[]
        {
            new Scenario("CYPMD_KS4June_SEED001", RequestStatus.ReadyToSubmit, p[0]),
            new Scenario("CYPMD_KS4June_SEED002", RequestStatus.ReadyToSubmit, p[1]),
            new Scenario("CYPMD_KS4June_SEED003", RequestStatus.ReadyToSubmit, p[2]),
            new Scenario("CYPMD_KS4June_SEED004", RequestStatus.ReadyToSubmit, p[3]),
            new Scenario("CYPMD_KS4June_SEED005", RequestStatus.ReadyToSubmit, p[4]),
            new Scenario("CYPMD_KS4June_SEED006", RequestStatus.ReadyToSubmit, p[5]),
            new Scenario("CYPMD_KS4June_SEED007", RequestStatus.ReadyToSubmit, p[5]), // duplicate of SEED006
            new Scenario("CYPMD_KS4June_SEED008", RequestStatus.SubmittedUnCommitted, p[6]),
            new Scenario("CYPMD_KS4June_SEED009", RequestStatus.ReadyToSubmit, p[6]), // duplicate of already-submitted SEED008
            new Scenario("CYPMD_KS4June_SEED010", RequestStatus.InProgress, p[7]),
            new Scenario("CYPMD_KS4June_SEED011", RequestStatus.InProgress, p[8])
        };

        await SeedScenariosAsync(requestRepository, requestStateBlobClient, checkYourPupilDataService,
            windowId, scenarios, DateTime.UtcNow);
    }

    // Last year's closed KS4 June window: half as many rows as the open window, and only the
    // statuses a window can be left in once its requests have been committed — nothing stays
    // InProgress / ReadyToSubmit / SubmittedUnCommitted after commit. Gives the establishment
    // Amendment requests grid a window whose rows are view-only (no Edit link).
    private static async Task SeedClosedWindowAsync(
        IPupilDataBlobClient pupilClient,
        IRequestRepository requestRepository,
        IRequestStateBlobClient requestStateBlobClient,
        ICheckYourPupilDataService checkYourPupilDataService)
    {
        var windowId = DevDataSeeder.ClosedKeyStage4JuneCheckingWindowId;

        var p = await GetIncludedPupilsAsync(pupilClient, windowId, required: 6);
        if (p is null) return;

        var scenarios = new[]
        {
            new Scenario("CYPMD_KS4June_CLOSED001", RequestStatus.SubmittedCommitted, p[0]),
            new Scenario("CYPMD_KS4June_CLOSED002", RequestStatus.SubmittedCommitted, p[1]),
            new Scenario("CYPMD_KS4June_CLOSED003", RequestStatus.SubmittedCommitted, p[2]),
            new Scenario("CYPMD_KS4June_CLOSED004", RequestStatus.Withdrawn, p[3]),
            new Scenario("CYPMD_KS4June_CLOSED005", RequestStatus.NotSubmitted, p[4]), // draft left unsubmitted at commit
            new Scenario("CYPMD_KS4June_CLOSED006", RequestStatus.NotSubmitted, p[5])
        };

        // SeedCheckingWindows dates this window a year back, so the rows must be dated inside
        // it — otherwise they sort above the live window's rows on the establishment grid.
        await SeedScenariosAsync(requestRepository, requestStateBlobClient, checkYourPupilDataService,
            windowId, scenarios, DateTime.UtcNow.AddYears(-1));
    }

    // Seeded change requests are KS4-only, so both dev windows read as KS4June.
    private static async Task<List<IPupilRecord>?> GetIncludedPupilsAsync(
        IPupilDataBlobClient pupilClient, Guid windowId, int required)
    {
        var pupils = await pupilClient.GetPupilsAsync(windowId, Laestab, CheckingWindowType.KS4June);
        if (pupils is null || pupils.Count == 0) return null;

        var included = pupils
            .Where(p => PupilInclusion.IsKs4Included(p.Pincl))
            .DistinctBy(p => p.Id)
            .Take(required)
            .ToList();

        return included.Count < required ? null : included;
    }

    private static async Task SeedScenariosAsync(
        IRequestRepository requestRepository,
        IRequestStateBlobClient requestStateBlobClient,
        ICheckYourPupilDataService checkYourPupilDataService,
        Guid windowId,
        IEnumerable<Scenario> scenarios,
        DateTime timestamp)
    {
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        foreach (var scenario in scenarios)
        {
            await requestRepository.UpsertAsync(new ChangeRequestData
            {
                WindowId = windowId,
                ReferenceNumber = scenario.Reference,
                OrganisationUrn = Urn,
                PupilId = scenario.Pupil.Id,
                PupilUpn = scenario.Pupil.Identifier,
                PupilFirstname = scenario.Pupil.Firstname,
                PupilSurname = scenario.Pupil.Surname,
                Timestamp = timestamp,
                SubmittedById = SubmittedById,
                SubmittedByName = SubmittedByName,
                SubmittedByEmail = SubmittedByEmail,
                Status = scenario.Status,
                RequestType = RequestType.Amendment,
                RequestTypeDescription = RequestTypeDescription,
                AmendmentType = WhatToChange.Remove
            });

            var state = new RequestState
            {
                SelectedWhatToChange = WhatToChange.Remove,
                CheckingWindow = window,
                SelectedPupil = ToPupilDto(scenario.Pupil),
                SelectedPupilId = scenario.Pupil.Id.ToString(),
                SelectedPupilLabel = $"{scenario.Pupil.Firstname} {scenario.Pupil.Surname}",
                ReferenceNumber = scenario.Reference,
                QuestionAnswers = new Dictionary<string, QuestionAnswer>
                {
                    ["reason"] = new() { TextValue = ReasonValue },
                    ["dual-registered-moved-dfe-number"] = new() { TextValue = ReasonDfeNumber }
                },
                QuestionHistory = ["select-pupil", "reason", "dual-registered-moved"]
            };

            await requestStateBlobClient.SaveAsync(windowId, scenario.Reference, state);
        }
    }

    // Mirrors CheckYourPupilDataRepository.ToPupilDto (IPupilRecord -> PupilDto).
    private static PupilDto ToPupilDto(IPupilRecord p) => new()
    {
        Id = p.Id,
        Surname = p.Surname,
        Firstname = p.Firstname,
        Sex = p.Sex,
        DateOfBirth = PupilDateFormatter.ToDisplayDate(p.DateOfBirth),
        Age = p.Age,
        Cypmd_Id = p.Cypmd_Id,
        Identifier = p.Identifier,
        Pincl = p.Pincl ?? 0,
        MatchRef = p.MatchRef,
        Laestab = p.Laestab,
        EntryDate = p.EntryDate
    };
}
