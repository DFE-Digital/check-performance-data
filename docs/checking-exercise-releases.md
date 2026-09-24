# Checking exercise releases: replace the data, keep the old data

*Design note: Dave Gouge, 2026-09-24. Implemented on branch `466-exercise-refactor-2`.*

## The requirement

A 16-19 results enquiry gets its data three times in one window:

1. The first set of results files (included and non-included), when the window opens.
2. A revised set, for example in February. The title of the data changes to add ": revised".
3. A second revised set, for example in March.

Each new set is a **full replacement** of the one before. The included and the non-included files
always change together.

Before this change, a new file overwrote the slot's previous file pointer, and the ingress run
overwrote the per-school output. The service lost the data that schools saw before. That data can
be necessary: a school may raise an enquiry against the first set, and a reviewer must see the data
that the school saw.

## The decision

Every clean ingress run of an exercise is a **release**:

- The run writes its per-school output under a **new prefix**. It never writes over the output
  that schools see.
- When the run finishes clean, one database save records the release and makes it **current**.
- Schools see the current release only. The earlier releases stay in storage.
- An admin can make an earlier release current again. There is no re-run and no re-upload.

We version the **whole exercise**, not each dataset slot. A run reads every complete slot and
publishes them together, and the journey's merged file combines several slots, so a release belongs
to a run, not to a slot. And for 16-19 the slots always change together.

We did not use one exercise row for each release (with `ReplacesCheckingExerciseId`). With that
option, journeys, Close, change requests and the kind-addressed admin routes would each need a
"head of the chain" rule. `FindExercise` would also throw when a window has two exercises of one
kind. A release inside one exercise changes none of these.

## The model

```text
CheckingExercise
  CurrentReleaseId ──► (no foreign key) one of its releases, or null
  Releases ──► CheckingExerciseRelease (Id, Number, PublishedAt, PublishedBy, FilesWritten)
                 Files ──► CheckingExerciseReleaseFile (a copy of each slot at run time)
ChangeRequest
  CheckingExerciseReleaseId ──► (no foreign key) the release that was live when the row was written
```

| Table | Purpose |
|---|---|
| `CheckingExerciseReleases` | One row for each clean run. `Number` is 1, 2, 3 and so on in each exercise, with a unique index on (exercise, number). |
| `CheckingExerciseReleaseFiles` | For each slot, the slot id (`DatasetId`), the CSV and schema that the run read, with their checksums, `Included`, `SourceFile` and `FeedsJourney`. This is a **copy**. The slot can take a new file after the run, and the release must still name its own files. |

`CheckingExercise.CurrentReleaseId` and `ChangeRequest.CheckingExerciseReleaseId` have **no foreign
key**. A key from the exercise to the release, plus the key from the release back to the exercise,
is a cycle that EF cannot order when it deletes an exercise. Only
`CheckingExerciseDefinitionRepository` writes `CurrentReleaseId`, and it always sets it to a
release of the same exercise.

Two migrations:

- `AddCheckingExerciseReleases` adds the two tables and the two nullable pointers. It changes no
  data. Every existing exercise starts with no release.
- `AddExerciseLayoutAndJourneySlots` adds `CheckingExercises.Layout`,
  `CheckingWindowDatasets.FeedsJourney`, and `DatasetId` / `FeedsJourney` on the release files. It
  marks every existing slot on an exercise with a kind as a journey slot, and sets `Layout =
  Vertical` on the display-only `Summary` exercises (the only ones whose schemas asked for it). It
  does not backfill `DatasetId`: see "Releases from before per-dataset files" below.

## The storage layout

All paths are in `CheckingExerciseBlobPaths`.

| What | Path |
|---|---|
| Output, exercise with no release | `exercises/{exerciseId}/data/{laestab}_{type}.json` (unchanged) |
| Merged output of one release (journey slots only) | `exercises/{exerciseId}/releases/{releaseId}/data/{laestab}_{type}.json` |
| One dataset's output in one release | `exercises/{exerciseId}/releases/{releaseId}/datasets/{datasetId}/{laestab}.json` |
| Uploaded CSV or schema | `ingress/{exerciseId}/{datasetId}/{first 16 checksum characters}/{file name}` |
| Run summaries and error log | `exercises/{exerciseId}/logs/` (unchanged; summaries already have a timestamp) |

- **No blob migration.** With no release, the reader uses the unversioned prefix. This is where
  every run before this change wrote, and where the dev seeders still write.
- **The checksum folder** stops an upload from overwriting an earlier upload that has the same file
  name. A release names its input files, so those files must stay where they are.
- Two release prefixes never contain each other, and neither contains the unversioned prefix. A
  test pins this.
- Legacy rows (`UsesExerciseStorage = false`) do not get releases. Their readers do not know the
  release prefix. The ingress stamps them as before.

## Journey data and display data

A release writes two kinds of file:

- **One file per dataset per school**, for every slot. The tab and its downloads read these. Each
  dataset's rows are in their own file, so the page never has to guess which dataset a row came
  from, and datasets with unrelated schemas never share a file.
- **One merged file per school**, from the **journey slots** only. The pupil-data and results
  enquiry journeys read this file, as before. A display-only exercise (no kind) has no journey
  slots, so its release writes no merged file.

A slot is a journey slot when `CheckingWindowDataset.FeedsJourney` is true. It is set once, when the
slot is created, and no update changes it:

| Exercise | Slots it starts with | A slot the admin adds with "Add data file" |
|---|---|---|
| Pupil data checking | None | Feeds the journey: merged into the pupils data (one file for KS4, two for 16-19) |
| Results enquiry | One supplier slot per results file, which feeds the journey | Display only |
| Data share (no kind) | None | Display only (there is no journey) |

The rule is in `WindowDatasets.DefaultsFor` and `WindowDatasets.AddedSlotFeedsJourney`. So **only a
new version of a journey data file changes what a journey reads.** A file added to a results
exercise is shown on its tab, but its rows never become results that a school can raise an enquiry
against.

Saving a window (`WindowService.UpdateAsync`) keeps every slot an exercise has. It adds any supplier
slot the window type needs, and removes only a supplier slot of another window type
(`WindowDatasets.IsStaleSupplierSlot`), for example a KS4 results tag after the window becomes 16-19.
Before this, saving rebuilt the slots from the defaults alone and silently dropped every slot an
admin had added.

A run with no release (a legacy row, or the kind-addressed processor path) keeps the old behaviour:
it merges every dataset into one file and writes no per-dataset files.

A release run also writes no per-dataset file when the exercise has **one** dataset and that
dataset feeds the journey (KS4 pupil data, for example). The merged file already holds exactly that
dataset's records, so a per-dataset file would be a copy. `ExerciseTabBuilder` reads the merged
file for such a release and puts every row under that one dataset. The rule is
`CheckingExerciseBlobPaths.MergedFileIsDatasetFile`, which the run and the tab builder both call.
A release written before this rule also has the copy. It is not read, and its content is the same.

### Releases from before per-dataset files

A release published before `AddExerciseLayoutAndJourneySlots` wrote only the merged file. Its
release files have an empty `DatasetId`. `ExerciseTabBuilder` reads per-dataset files only when the
exercise has a release **and** every published dataset has an id; otherwise it reads the merged file.
The next validation of such an exercise writes a normal release.

## The layout

The exercise decides how its tab looks: `CheckingExercise.Layout`, set on the exercise edit page
("How schools see the data"): **Table** (dataset selector, search, paging) or **Summary list** (one
record per school, as labels and values). Every dataset of one exercise uses that layout. A schema's
`x-display.layout` is no longer read. A summary-list exercise shows the first record of its first
dataset.

## The flow

```text
Admin uploads new CSV + schema into each slot   (slot pointers change; nothing schools see changes)
        │
Admin clicks "Validate data"
        │
CheckingExerciseIngress.ProcessAsync
  ├─ new releaseId = Guid.NewGuid()
  ├─ CsvSchemaFileProcessor writes, under exercises/{id}/releases/{releaseId}/,
  │     datasets/{datasetId}/… for every slot and data/… from the journey slots only
  │     (validates every slot first; on a write error removes what it wrote; no clear sweep)
  └─ clean finish → CheckingExerciseDefinitionRepository.PublishReleaseAsync
        one SaveChanges: insert release + files, CurrentReleaseId = releaseId, validation stamp
        │
Schools see the new release on their next read
```

A run that fails writes no release row, so the current release does not change. The processor
removes the files it wrote after a write error. A cancelled run can leave files under its release
prefix, but no row points to them, so no reader finds them.

`clearExistingFiles` does nothing on a release run. The new prefix is always empty.

## What reads the current release

| Reader | How it finds the release |
|---|---|
| `CheckingDataReader.ReadDatasetAsync` (exercise tabs and their downloads) | `CheckingDataExercise.CurrentReleaseId` + the dataset id |
| `CheckingDataReader.ReadAsync` (tabs with no release, or a release from before per-dataset files) | `CheckingDataExercise.CurrentReleaseId` |
| `PupilDataBlobClient` | `ICheckingExerciseStorageResolver` → `CurrentReleaseId` |
| `StudentResultsBlobClient` (results enquiry journey) | as above |
| `ExerciseTabBuilder` (titles, columns, layout) | `CheckingExerciseDto.PublishedDatasets`: the schemas of the current release |

**The display reads the schemas of the release, not of the slots.** An admin can upload the revised
file and its schema (with the new ": revised" title) some time before running it. Until the run
publishes, schools must still see the old title with the old data. `PublishedDatasets` returns the
files of the current release, or the complete slots when there is no release.

### Caches

- `StudentResultsBlobClient` already put the blob path in its cache key. The path now includes the
  release, so a new release is a new key.
- `CheckYourPupilDataRepository` used `pupils:{windowId}:{laestab}`. The key now also has the
  current release of each pupil-data exercise. This costs one small indexed query on each read,
  and a new release is seen at once, not after the 30-minute sliding expiry.

## The admin screens

On the exercise edit page (`admin/windows/{id}/exercises/{exerciseId}/edit`), the Data tab has a
**Data releases** table. It shows each release (newest first), when it was published (UTC) and by
whom, the CSV of each slot, and which release is **Live**.

"Make live" opens a confirmation page (`ExerciseReleaseController`,
`.../releases/{releaseId}/make-live`, GET then POST, gated on `ManageWindow`). The page names the
release that goes live and the one it replaces. `ICheckingExerciseReleaseService.MakeLiveAsync`
refuses a release that is not of that exercise, or an exercise that is not of that window.

The exercise page's "Validate data" button now goes to the exercise-id route for every exercise.
The kind-addressed route (`admin/windows/{id}/{exercise}/validate`) also sends a new-storage row
through the ingress. Before this change it called the processor with no exercise id, so a run of
a new row wrote to the kind-based paths, and no reader reads those paths for such a row.

## Change requests

`RequestService` writes `ChangeRequest.CheckingExerciseReleaseId` on every save and submit. It reads
the value through the same resolver that the journey's readers use, at the time of the write. It
does not read it from the session, because a draft can be resumed weeks later, after a new release.

Limit: if a release changes while a school is part of the way through a journey, the stamp names
the release at the time of the save. It does not name the release at the start of the journey.

## Rules for later changes

- **Do not write over a release's output.** A run always gets a new release id.
- **Do not make a release current before its run finishes clean.** `PublishReleaseAsync` is the
  only place that sets `CurrentReleaseId` after a run.
- **The display must read `PublishedDatasets`, not `DatasetsToIngest`.** Otherwise an unrun schema
  reaches schools.
- **Do not delete release rows or their blobs** until there is an agreed retention period (see
  below).
- **Only journey slots feed a journey.** `FeedsJourney` is set when a slot is created (by
  `WindowDatasets`) and never updated.
- **Saving a window must never drop an admin-added slot.**
- **The display reads each dataset's own file.** Do not go back to working out a row's dataset
  from its fields.
- **Keep `x-ingress.collection` the same** in each version of a schema. It is the key of the
  dataset in the dropdown and in the download route. Change only the labels (`x-display.section`,
  `x-download.label`, `x-download.fileName`).

## Not in this change

- **Retention and clean-up.** Old releases hold pupil and results data. They stay in storage until
  there is a retention period that data protection agrees. After that, a clean-up job can delete
  releases that are older than the period and are not current.
- **"Both slots together" is not enforced in code.** For 16-19 results, both files always change
  together. A pupil-data exercise can correctly re-run after one file changes, so a rule would
  block a valid run. The release table shows which files each release read.
- **No admin view of change requests by release.** The release id is recorded, but no screen
  groups or filters by it yet.

## Tests

| Test | What it pins |
|---|---|
| `CheckingExerciseBlobPathsTests` | Release paths, the unversioned fallback, prefixes that do not overlap, the checksum folder |
| `CheckingExerciseReleaseTests` (unit) | `CurrentRelease`, `PublishedDatasets`, and the make-live service rules |
| `CheckingExerciseIngressTests` | A clean run publishes one release, under the same id it wrote to; each run has a new id; legacy rows are stamped with no release; a failed run publishes nothing |
| `ExerciseTabBuilderTests` | The tab reads the schema of the release, not a newer slot upload; each dataset shows only its own rows; an older release is read from its merged file; the exercise decides the layout and a schema cannot; the raw download holds each dataset under its name |
| `WindowDatasetDefaultsTests`, `WindowDatasetsTests`, `ExerciseDataControllerTests` | Pupil data and shares start empty; results supplier slots feed the journey; an added pupil-data file feeds it and an added results or share file does not |
| `WindowExercisesTests` | Saving a window keeps every added slot and replaces only stale supplier slots |
| `ExerciseReleaseControllerTests`, `ValidateWindowControllerTests` | Confirm and make live; the kind route on a new row goes through the ingress |
| `ChangeRequestReleaseStampTests` | A request records the live release |
| `CheckingExerciseReleaseTests` (integration, Postgres + Azurite) | A full replacement goes live at once through the real results reader and cache; the first release's output and files are kept; make live again with no re-run; a failed run changes nothing; an admin-added slot gets its own file and never reaches the journey's file |
| `SeededCheckingExerciseTests` | The dev seed's ingress runs publish releases |
