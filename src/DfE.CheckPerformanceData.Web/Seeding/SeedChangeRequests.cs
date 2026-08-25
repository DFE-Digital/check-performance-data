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

    public static async Task ExecuteSeedAsync(
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

        var included = pupils
            .Where(p => PupilInclusion.IsKs4Included(p.Pincl))
            .DistinctBy(p => p.Id)
            .Take(9)
            .ToList();

        if (included.Count < 9) return;

        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);

        var p = included; // p[0]..p[8]

        var scenarios = new[]
        {
            (Reference: "CYPMD_KS4June_SEED001", Status: RequestStatus.ReadyToSubmit, Pupil: p[0]),
            (Reference: "CYPMD_KS4June_SEED002", Status: RequestStatus.ReadyToSubmit, Pupil: p[1]),
            (Reference: "CYPMD_KS4June_SEED003", Status: RequestStatus.ReadyToSubmit, Pupil: p[2]),
            (Reference: "CYPMD_KS4June_SEED004", Status: RequestStatus.ReadyToSubmit, Pupil: p[3]),
            (Reference: "CYPMD_KS4June_SEED005", Status: RequestStatus.ReadyToSubmit, Pupil: p[4]),
            (Reference: "CYPMD_KS4June_SEED006", Status: RequestStatus.ReadyToSubmit, Pupil: p[5]),
            (Reference: "CYPMD_KS4June_SEED007", Status: RequestStatus.ReadyToSubmit, Pupil: p[5]), // duplicate of SEED006
            (Reference: "CYPMD_KS4June_SEED008", Status: RequestStatus.SubmittedUnCommitted, Pupil: p[6]),
            (Reference: "CYPMD_KS4June_SEED009", Status: RequestStatus.ReadyToSubmit, Pupil: p[6]), // duplicate of already-submitted SEED008
            (Reference: "CYPMD_KS4June_SEED010", Status: RequestStatus.InProgress, Pupil: p[7]),
            (Reference: "CYPMD_KS4June_SEED011", Status: RequestStatus.InProgress, Pupil: p[8])
        };

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
                Timestamp = DateTime.UtcNow,
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