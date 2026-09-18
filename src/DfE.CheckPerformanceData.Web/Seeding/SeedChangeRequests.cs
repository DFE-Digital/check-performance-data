using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>How many requests this seeder writes. Their references run 001..this.</summary>
    public const int SeededRequestCount = 11;

    private const string ReferencePrefix = "CYPMD_KS4June_SEED";

    private static string Reference(int number) => $"{ReferencePrefix}{number:000}";

    /// <summary>
    /// The references of the rows this seeder writes — the baseline a dev environment is reset
    /// back to, and the single thing that decides which requests survive
    /// <see cref="ResetToSeedBaselineAsync"/>. Derived from the same generator the rows are
    /// written with, so the two cannot drift.
    /// </summary>
    public static readonly IReadOnlySet<string> SeededReferenceNumbers =
        Enumerable.Range(1, SeededRequestCount).Select(Reference).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Deletes every change request that is not part of the seeded baseline, and returns how many
    /// went. This is what the browser suite calls between journeys to get back to a known state.
    ///
    /// Membership of <see cref="SeededReferenceNumbers"/> rather than a pattern, for two reasons.
    /// The seeded rows share the CYPMD prefix with the ones a journey submits, so no prefix
    /// separates them; and '_' is a single-character wildcard in SQL LIKE, so a pattern spelt the
    /// obvious way silently matches references that merely resemble a seeded one.
    ///
    /// It replaces a filter on 'DEV-%', a prefix minted only by the dev pipeline harness. A
    /// request submitted through a journey is referenced CYPMD_{type}_{id} and so was never
    /// matched: the reset answered "deleted 0" while its callers believed state had been cleared.
    /// Harmless for most journeys, fatal for the merge journey, whose duplicate guard refuses a
    /// student who already has a request — that suite passed the first time it ran against an
    /// environment and failed on every run after.
    /// </summary>
    public static Task<int> ResetToSeedBaselineAsync(
        IPortalDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // Materialised to an array first: EF cannot translate Contains on IReadOnlySet, but it
        // renders the array form as a SQL parameter list. The set stays the public shape because
        // membership is what the baseline means.
        var baseline = SeededReferenceNumbers.ToArray();

        return dbContext.ChangeRequests
            .Where(r => !baseline.Contains(r.ReferenceNumber))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public static async Task ExecuteSeedAsync(
        IPupilDataBlobClient pupilClient,
        IRequestRepository requestRepository,
        IRequestStateBlobClient requestStateBlobClient,
        ICheckYourPupilDataService checkYourPupilDataService,
        ICheckingExerciseService checkingExerciseService)
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
            (Reference: Reference(1), Status: RequestStatus.ReadyToSubmit, Pupil: p[0]),
            (Reference: Reference(2), Status: RequestStatus.ReadyToSubmit, Pupil: p[1]),
            (Reference: Reference(3), Status: RequestStatus.ReadyToSubmit, Pupil: p[2]),
            (Reference: Reference(4), Status: RequestStatus.ReadyToSubmit, Pupil: p[3]),
            (Reference: Reference(5), Status: RequestStatus.ReadyToSubmit, Pupil: p[4]),
            (Reference: Reference(6), Status: RequestStatus.ReadyToSubmit, Pupil: p[5]),
            (Reference: Reference(7), Status: RequestStatus.ReadyToSubmit, Pupil: p[5]), // duplicate of Reference(6)
            (Reference: Reference(8), Status: RequestStatus.SubmittedUnCommitted, Pupil: p[6]),
            (Reference: Reference(9), Status: RequestStatus.ReadyToSubmit, Pupil: p[6]), // duplicate of already-submitted Reference(8)
            (Reference: Reference(10), Status: RequestStatus.InProgress, Pupil: p[7]),
            (Reference: Reference(11), Status: RequestStatus.InProgress, Pupil: p[8])
        };

        foreach (var scenario in scenarios)
        {
            await requestRepository.UpsertAsync(new ChangeRequestData
            {
                WindowId = windowId,
                CheckingExerciseId = checkingExerciseService.IdFor(
                    window.Exercises,
                    WhatToChangeCheckingExerciseMap.CheckingExerciseFor(WhatToChange.Remove)),
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
