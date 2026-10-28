# Blob storage

Where pupil, results and exercise data live in blob storage. See also [checking-exercise-releases.md](checking-exercise-releases.md) for the release layout.

## Pupil Storage

Pupils are stored as **one JSON file per school in blob storage**, supplied by an external service: container `{windowId}`, blob `data/{laestab}_pupils.json` (the `/` in a laestab is stripped, so `933/4290` → `data/9334290_pupils.json`). That `data/` is the **pupil-data checking exercise's prefix** — see **Checking exercise blob layout** below; every path-naming method takes a `CheckingExerciseType`. The file is a JSON array of `PupilRecord`; each record carries a stable `Id` (Guid). There is no `Pupils` database table.

`IPupilDataBlobClient` (Application, implemented by `Infrastructure/BlobStorage/PupilDataBlobClient.cs`) reads the file (`GetPupilsAsync` → `null` when missing; malformed JSON is allowed to throw), checks existence (`HasPupilDataAsync`), and writes it for dev seeding (`UploadPupilsAsync`). `CheckYourPupilDataRepository` loads a school's file via this client, **caches it in `IMemoryCache`** (`pupils:{windowId}:{laestab}`, 30-min sliding expiry), and does all filtering / search / paging / the included-vs-non-included split **in memory**. It still uses `IPortalDbContext` for `CheckingWindows` and for the `ChangeRequests` UPN-exclusion query in `SearchPupilsAsync`.

Pupil identity throughout the journey is the `Guid Id` from the JSON. Repository methods key on **`laestab`** (which scopes the file to a school); `SearchPupilsAsync` also takes `urn` purely for the `ChangeRequests` exclusion query, which matches on **UPN** (`ChangeRequest.PupilUpn`), never on `Id`.

`HasPupilData` on the landing page is set by `LandingPageService` through `ICheckingDataReader.HasSchoolDataAsync` per live exercise — see [Landing page](domain-model.md#features).

The school's laestab reaches the services via an `organisation_laestab` claim (added in `ClaimsEnrichmentService` from the DfE Sign-in organisation, exposed as `ICurrentUserService.OrganisationLaestab`).

## Checking exercise blob layout

`Application/WindowManagement/CheckingExerciseBlobPaths.cs` is the only description of where a checking window's blobs live. Two exercises share one `{windowId}` container, so each owns a prefix and an ingress run belongs to an exercise, not to the window — otherwise one exercise's clear sweep destroys another's output and data that was never at fault has to be re-uploaded.

| Exercise | Prefix | Data files | Summaries / error log |
|---|---|---|---|
| `PupilData` | *(bare)* | `data/{laestab}_pupils.json` | `{windowId}_summary_{ts}.csv`, `{windowId}_error_log.txt` |
| `ResultsEnquiry` | `results-enquiry/` | `results-enquiry/data/{laestab}_results.json` | `results-enquiry/{windowId}_summary_{ts}.csv`, `results-enquiry/{windowId}_error_log.txt` |

Two rules the lookup exists to enforce:

- **The prefix is a kebab-case slug, never the enum's `ToString()`.** `$"{exercise}/"` would emit `ResultsEnquiry/` and orphan every results blob already written.
- **There is no default case.** A new `CheckingExerciseType` with no prefix throws `ArgumentOutOfRangeException` rather than silently sharing another exercise's prefix.

**Pupil data keeps the bare prefix on purpose**, which is why #316 needed no blob migration: blob prefixes match as plain strings, so a `data/` sweep never reaches `results-enquiry/data/`. Do not tidy pupil data under a `pupil-data/` prefix without budgeting a migration of every window's blobs.

**Every dataset slot belongs to an exercise, and stamps by file of origin (#324).**
`WindowDatasets.DefaultsFor(windowType, exerciseType)` decides the slots: pupil data takes the
supplier's pupil files (two on Post16), and a results enquiry takes one per source file, named by
the `ResultsFileTags` tag it stamps — seven on Post16, four on KS4, none on KS2, which has no results
feed. **A schema property may name the CSV column it is read from**, or a list of columns joined with
`x-ingress.separator` (`x-ingress.source`, copied by
`CsvSchemaFileProcessor` before undeclared columns are dropped): that is how the supplier's late results
columns (`GNUMBER`, `BRDSUBNO`, `EXAM_YEAR_SEASON`/`SEASON_AND_YEAR`, `SYLLABUS_TITLE`/`QUALIFICATION_DESCRIPTION`)
reach the journey as `QAN`, `SYLLABUS`, `SESSION` and `QUAL_NAME`; in the 16-18 results data file
`SESSION` is `SEASON` + `EXAMYEAR` and `QUAL_NAME` is `Short_Qual_Desc` + `SubjectDescription`. Never add supplier names to
`ResultRecord`. A late `Amendment` row is kept beside the result it corrects. `ResultsSources` (Application) is the only list of results files: tags in arrival order, the label
schools see. Schools see the label, never the tag. A dataset's `SourceFile` is the exact analogue of its `Included`: `Included` stamps inclusion
by file of origin, `SourceFile` stamps provenance, and both are stamped by the processor before
validation and guarded by the schema declaring the column (`INCLUDED` / `SOURCE`), because
`AllowAdditionalProperties` is false. A third stamp, `INCLUDABLE` (#711), says whether a school may
ask to include the pupil: true when the record is not included, or when its code is one the
schema's `x-ingress.includable` rule names (the 16-19 included file names `P_INCL` 502). It is
derived from `INCLUDED`, so it follows the same guard. The Include journey reads it. A run writes
`CheckingExerciseBlobPaths.DataBlobName(exercise, laestab)` — never `PupilsBlobName` directly: the
two normalise the laestab differently on purpose. Every feed must carry a `LAESTAB` column, or the column its schema's `x-ingress.institutionKey` names
(`LAESTAB_0` in the previously published file): it is what splits one supplier file into one blob per school; a file without it fails the run by name.

**A slot may be optional (#324).** Only the files a results enquiry starts with are required (`ResultsSource.IsRequired`:
16-19 included, non-included and late results 1; KS4 main); the others
arrive weeks apart and one may never arrive, so `CheckingExerciseDto` asks
`InUse.Any(IsComplete) && InUse.Where(Required).All(IsComplete)` and a run reads
`DatasetsToIngest` — the complete slots in use only.

**A slot may be retired.** `CheckingWindowDataset.Retired` takes a slot whose file was replaced out of
every run, the required check and the validation checksums (16-19 February: the revised files replace
included, non-included and both late files; March: retention replaces included revised). It is kept,
not deleted, because releases name it; the Data tab retires it and puts it back through a
confirmation page. The late-results guidance shows while the results enquiry's **Show late results warning**
box is ticked (`CheckingExercise.ShowLateResultsWarning`, read only by `LateResultsWarning`). It is set by the
admin on the exercise edit page, not derived from the files, and a new results enquiry starts ticked
(`WindowExercises.ShowsLateResultsWarningByDefault`). Full detail: `docs/results-enquiry.md`. A run rewrites the exercise's whole output, so the
exercise is simply re-run when the next file lands. Every pupil-data slot stays required: both 16-19
pupil files carry a whole population, and a run missing one writes a blob missing half the school.

`ICsvSchemaFileProcessor.ProcessAsync` and every path-naming method on `IPupilDataBlobClient` (`GetPupilsAsync`, `HasPupilDataAsync`, `ListSchoolLaestabsAsync`, `UploadPupilsAsync`) take a `CheckingExerciseType`. `IResultsClient` does not — its exercise is implied by the class — but `ResultsEnquiryBlobPaths` derives its paths from the shared helper rather than describing the layout a second time.

