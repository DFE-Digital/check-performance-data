# Post-16 workbook schemas

This folder holds the 16-19 supplier schemas, named `{name}_schema.json`. It holds no CSVs: the dev seed generates its sample files (`SeedPost16OctoberSamples` and the later `SeedPost16*Samples`).

These row schemas transcribe the field references, SQL types, CSV column letters and supplied headings in `Post16.ods`. `post16-ingress_schema.json` defines one combined document per institution. A collection name identifies the source dataset, so a CSV download can select that collection without guessing from overlapping fields.

Each row schema uses the same extensions:

- `x-ingress.collection` is the key in the combined JSON; `pageTab` groups collections on the page; `institutionKey` identifies the source field used to split incoming records by institution.
- `x-download.fileName` and `properties.*.x-csv.columns` identify the separate CSV and its column order. The variant keys distinguish provisional/revised from retention columns where the workbook does.
- `x-display.section` gives the selector label; `properties.*.x-display` gives the page label, visibility, order and, for names, searchability. Only selected pupil identity fields are marked visible by default. Other fields remain available for a details view.

For example, `students-included`, `students-non-included` and `students-previously-published` all have `pageTab: students`, but occupy separate collections and CSV files. `students-value-added` and `students-aims` also belong to that tab. The screenshot's Subject column has no field in the pupil sheets; it would need a defined join to results and a choice of how to handle multiple results per student.

This is a schema and display contract. The current `CsvSchemaFileProcessor` writes a flat merged array per school and requires `LAESTAB` in every source. To persist this envelope, ingress must tag each dataset and group rows into these collections. Ingress splits rows by the column `x-ingress.institutionKey` names (default `LAESTAB`), so the previously published file splits by `LAESTAB_0`. The downloads page must read the collection metadata and produce the workbook's CSV headers/order. The schemas alone do not change those runtime paths.

The workbook does not define JSON nullability or required values consistently across its exercise variants. The row schemas allow null for source fields and leave `required` unset. Fields without a CSV column remain in the schema with an empty `x-csv.columns` map and should not be exported. The included pupil sheet marks six T level core/OS fields removed, and the included results sheet strikes through `SCHCNO` and `EXAMCAND` while reusing their column letters; those fields are excluded from CSV export metadata. The included pupil sheet also has a heading-only inclusion description row without a field reference, so it is not a JSON property.

## Results schema and the dev seed

`results-included_schema.json` carries one field the workbook does not: `SOURCE`. The processor stamps the dataset slot's tag (`16to19_INC` and so on) on every record, and only does so when the schema declares the column, because `additionalProperties` is false. It has no CSV column, so it is not exported. The schema also marks the student and result columns visible, with the names searchable, so the Results tab renders a table.

`results-included_schema.json` and `results-non-included_schema.json` are the 16-18 included and non-included results data files, as their data specifications define them (fields 1-32, headed by field reference; the descriptive "column heading" is the heading of the CSV a school downloads, in column order A-AD). The two differ in the `CAPPED_PTS` download heading ("Maths"/"Math"), so each file has its own schema. The results schemas (these two and `results-late_schema.json`) set **no `maxLength`**: the specifications' lengths were written for SQL tables, and in a CSV import one over-long value would fail validation and stop the whole run. `SCHCNO` and `EXAMCAND` are struck through in the workbook and are not exported. Each schema adds the four values the enquiry journey reads, which are not CSV columns: `x-ingress.source` fills them from the supplier columns — `QAN` from `GNUMBER`, `SYLLABUS` from `BRDSUBNO`, `SESSION` from `SEASON` + `EXAMYEAR` joined (`S2025`), and `QUAL_NAME` from `Short_Qual_Desc` + `SubjectDescription` joined with a space (`GCE A English`). They repeat supplier columns, so they are neither downloaded nor shown on the Results tab.

The "16 to 19 Oct" dev window uses these schemas: the dev seed (`SeedPost16OctoberSamples`) imports and validates that window's sample CSVs, and also writes them to the ingress storage account, container `16-to-19-oct`, for an admin to import by hand. Pair `students/included.csv` with `students-included_schema.json`, `students/nonincluded.csv` with `students-non-included_schema.json`, `results/16to19_INC.csv` with `results-included_schema.json`, `results/16to19_NONINC.csv` with `results-non-included_schema.json`, and `results/16to19_LR1.csv` with `results-late_schema.json`.

## Late results schema and column renaming

`results-late_schema.json` is the 16-19 late results file as the data specification defines it (1618 names): `LAESTAB`, `CYPMD_ID`, `SURNAME`, `FORENAMES`, `AB_CODE_NDAQ`, `Short_Qual_Desc`, `EXAM_YEAR_SEASON`, `EXAM_DATE`, `Discount_Code`, `SYLLABUS_TITLE`, `GNUMBER`, `BRDSUBNO`, `GRADE` and `Late_Result_Type` (`Amendment` or `New`). It is for late results 1. Late results 2 has `results-late-2_schema.json`: the same columns, but its own `x-ingress.collection`, download file and section, because the Results tab keys a dataset by its collection and one schema for both files would merge them into one dataset. The "16 to 19 Nov" dev window (`SeedPost16NovemberSamples`) has `results/16to19_LR2.csv` (ingress container `16-to-19-nov`) validated with this schema; see `docs/testing-late-results-2.md`. The "16 to 19 Feb" dev window (`SeedPost16FebruarySamples`) also has the revised files validated (ingress container `16-to-19-feb`): `results/16to19_INC_REV.csv` with `results-included-revised_schema.json` and `results/16to19_NONINC_REV.csv` with `results-non-included-revised_schema.json`. Those two schemas are copies of the included and non-included results schemas with their own `x-ingress.collection`, download file and section, for the same reason. The "16 to 19 Mar" dev window (`SeedPost16MarchSamples`) also has `results/16to19_INC_REV_RET.csv` (ingress container `16-to-19-mar`) validated with `results-included-revised-retention_schema.json`, a copy of the included revised schema with its own collection, download file and section. See `docs/testing-revised-results.md`. The KS4 version, with the KS4 names (`FORENAME`, `QUALIFICATION_TYPE`, `SEASON_AND_YEAR`, `WOLF_DISC_CODE`, `QUALIFICATION_DESCRIPTION`), is `../ks4autumn/results-late_schema.json`.

Ingress writes each JSON key with the property's name. A property may set `x-ingress.source` to the CSV column it is read from — or to a list of columns, joined with `x-ingress.separator` (default none), blank parts skipped — so a supplier column reaches the journey under the name the journey reads: `QAN` ← `GNUMBER`, `QUAL_NAME` ← `SYLLABUS_TITLE` / `QUALIFICATION_DESCRIPTION`, `SYLLABUS` ← `BRDSUBNO`, `SESSION` ← `EXAM_YEAR_SEASON` / `SEASON_AND_YEAR`. It is a copy: a CSV that already has the property's own column keeps it, and the source column is then dropped like any column the schema does not declare.

An `Amendment` row does not replace the result it corrects. Both rows reach the journey, and the amendment carries its late file's tag, so the search shows the original ("File: Included") and the amendment ("File: Late results 1").

## Previously published schema

`students-previously-published_schema.json` is the 16-19 previously published data file as the data specification defines it: fields 1-10 and 13, headed by field reference, one row per student. The names end in `_0`, and there is no `LAESTAB` column, so the schema sets `x-ingress.institutionKey` to `LAESTAB_0` and ingress splits the rows by that column. `SCHCNO` is not shared and `cypmd_pk` has no column, so neither is exported. Like the results schemas, it sets no `maxLength`. The table shows surname and forename (searchable), sex, date of birth and CYPMD ID.

Every 16-19 dev window's pupil data checking exercise has a third slot, `previously-published`, with `FeedsJourney` false: the file is shown on the Students tab and downloaded, but it is not merged into the journeys' pupils file. `SeedPost16OctoberSamples` links `students/previously-published.csv` (ingress container `16-to-19-oct`) with this schema: every second included student. The October step fills it in every window; the February step retires it. To add this file by hand, add a data file to the pupil data exercise and choose "Data share only" for "What is this file for?"; a file added as "Pupil data for the journeys" is merged into the journeys' pupils file.

## Previously published revised schema

`students-previously-published-revised_schema.json` is a copy of the previously published schema with its own `x-ingress.collection`, download file (`ks5_prev_pub_rev_pup25.csv`) and section ("Previously published revised"), because the Students tab keys a dataset by its collection. The properties are the same.

Every 16-19 dev window's pupil data checking exercise also has a fourth slot, `previously-published-revised`, with `FeedsJourney` false. Like the results enquiry's revised slots, it is empty and not required until February; a required empty slot would stop the October run. The February step (`SeedPost16FebruarySamples`, so the "16 to 19 Feb" and "16 to 19 Mar" dev windows) links `students/previously-published-revised.csv` (ingress container `16-to-19-feb`) with this schema, makes the slot required, retires `previously-published` and validates pupil data again, so pupil data has two releases. In the "16 to 19 Oct" and "16 to 19 Nov" windows the slot stays empty. The file is October's, a little changed: every tenth student in October's file is not in it, every sixth included student that October left out is added, and the candidate numbers start at 2000.

## Value added schemas

`students-value-added_schema.json` is the 16-19 value added data file as the data specification defines it: fields 1-17, headed by field reference, one row per student per qualification. The descriptive column heading is the heading of the CSV a school downloads, in column order A-Q. Like the results schemas, it sets no `maxLength`. There is no `LAESTAB` column, so the schema sets `x-ingress.institutionKey` to `Laestab`. The table shows last name and first name (searchable), sex, CYPMD ID and subject. The file has no date of birth or age column, so the table cannot show them.

`students-value-added-revised_schema.json` (February) and `students-value-added-revised-retention_schema.json` (March) are copies with their own `x-ingress.collection`, download file and section, because the Students tab keys a dataset by its collection. The properties are the same. The download file names (`ks5va25.csv`, `ks5va_rev25.csv`, `ks5va_rev_ret25.csv`) are placeholders until the supplier confirms them.

Every 16-19 dev window's pupil data checking exercise has three value added slots, with `FeedsJourney` false: "Value Added", "Value Added: revised" and "Value Added: revised incl. retention". All wait empty and are not required, because a required empty slot would stop the October run. Each step (`SeedPost16ValueAdded`) links its file, makes its slot required, retires the slot before it and validates pupil data, so pupil data gets one release per step:

| Step | Slot | Sample file | Schema |
|---|---|---|---|
| November | Value Added | `students/value-added.csv` | `students-value-added_schema.json` |
| February | Value Added: revised | `students/value-added-revised.csv` | `students-value-added-revised_schema.json` |
| March | Value Added: revised incl. retention | `students/value-added-revised-retention.csv` | `students-value-added-revised-retention_schema.json` |

Each file has one row per included student per qualification; every third student has two. The revised files change the actual points, and so the value added score, of some rows. To add a value added file by hand, add a data file to the pupil data exercise and choose "Data share only" for "What is this file for?".

## Pupil aims schema

`students-aims_schema.json` is the 16-19 pupil aims data file as the data specification defines it: fields 1-15, headed by field reference, one row per student per learning aim. The descriptive column heading is the heading of the CSV a school downloads, in column order A-N. `cypmd_pk` has no column and is not exported. Like the results schemas, it sets no `maxLength`. Ingress splits the rows by `LAESTAB`, the student's institution; `AimLAESTAB` is where the aim was recorded, and it can be a different provider. The table shows surname and forename (searchable), qualification number, subject, aim type and where the aim was recorded.

The "16 to 19 Mar" dev window has a "Pupil aims" data share (tab "Aims"): a display-only exercise with no kind and one slot, `aims`, that feeds no journey. `SeedPost16MarchSamples` validates `students/aims.csv` (ingress container `16-to-19-mar`) with this schema: one or two aims for every included student, with every tenth aim recorded at another provider.

## Pupil campus schema

`students-campus_schema.json` is the 16-19 pupil campus data file as the data specification defines it: one row per student, headed by field reference, in column order A-AI. The first column, `CampID`, has no field number in the specification. The file gives the campus a student is allocated to, whether the student was allocated to the provider in each of the last three years, and the student's entries and point scores in each attainment programme (A level, academic, tech level, applied general, technical certificate and T level). The descriptive column heading is the heading of the CSV a school downloads. The provisional, revised and retention files have the same columns, so there is one schema. Like the results schemas, it sets no `maxLength`. Ingress splits the rows by `LAESTAB`. The table shows last name and first name (searchable), sex, date of birth, age and CYPMD ID. The download file name (`ks5campus25.csv`) is a placeholder until the supplier confirms it.

Every 16-19 dev window has a "Pupil campus" data share (tab "Campus"): a display-only exercise with no kind and one slot, `campus`, that feeds no journey. The October step (`SeedPost16PupilCampus`) validates `students/campus.csv` (ingress container `16-to-19-oct`) with this schema, and the later steps keep that release. The file has one row for every included student. Every school has two campuses, `{LAESTAB}1` and `{LAESTAB}2`; every fourth student is at the second.

## Summary schemas

The 1618 Summary Data sheet feeds four checking exercises over the year, and the supplier sends a different file shape for each. The sheet's three column-letter columns describe those shapes, and its four "Included in" flags say which exercise gets which:

| Exercise | Workbook column | Columns | Schema |
|---|---|---|---|
| Autumn CE | Column (Autumn) | 62 (A–BJ) | `summary-autumn_schema.json` |
| Provisional Value Added (November) | Column (November VA) | 102 (A–CX) | `summary-november-va_schema.json` |
| Light touch, revised data share (February) | Column (November VA) | 102 (A–CX) | `summary-november-va-revised_schema.json` |
| Retention (March) | Column (Retention) | 135 (A–EE) | `summary-retention_schema.json` |

Each schema lists only the fields its file carries, with one `default` column letter each, every field visible and `order` equal to the column position. There is one schema per exercise rather than one schema with variants, so an exercise's dataset is uploaded with the schema that matches its file and nothing has to record which variant an exercise uses. February's file has November's shape, so `summary-november-va-revised_schema.json` has the same properties as `summary-november-va_schema.json`. The workbook's Autumn column repeats `AZ` for `ENTRY_PER_M` and `T_SCOPEEX_E`; the schema numbers the column contiguously, so `T_SCOPEEX_E` onwards is one letter later than the sheet (BA–BJ). All four set `x-display.layout` to `vertical`: one record per school, pivoted into label/value rows. Each has its own section, which is the heading schools see ("Summary", "Summary with value added", "Summary with value added: revised", "Summary with value added: revised including retention"), and its own `x-ingress.collection` and download file, so each shows as its own dataset. The download file names (`summary.csv`, `summary-value-added.csv`, `summary-value-added-revised.csv`, `summary-value-added-revised-retention.csv`) are placeholders until the supplier confirms them.

Every 16-19 dev window has a "Summary" data share (tab "Summary", the first tab, layout `Vertical` on the exercise): a display-only exercise with no kind and four slots, one per step of the year, none of which feeds a journey. Only the first is required; the others wait empty until their step. Each step (`SeedPost16Summary`) links its file, makes its slot required, retires the slot before it and validates, so the share gets one release per step:

| Step | Slot | Sample file | Schema |
|---|---|---|---|
| October | Summary | `summary/summary.csv` | `summary-autumn_schema.json` |
| November | Summary with value added | `summary/summary-value-added.csv` | `summary-november-va_schema.json` |
| February | Summary with value added: revised | `summary/summary-value-added-revised.csv` | `summary-november-va-revised_schema.json` |
| March | Summary with value added: revised incl. retention | `summary/summary-value-added-revised-retention.csv` | `summary-retention_schema.json` |

A slot name is at most 50 characters, so the March slot says "incl." rather than "including"; its schema's section, which schools see, says "including". Each file has one row per school in the student files and every column of its schema, with made-up values in the column's shape. A column keeps its value from October to November and from February to March; the revised files change it.
