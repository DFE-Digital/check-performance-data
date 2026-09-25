# Post-16 workbook schemas

This folder holds the 16-19 supplier schemas, named `{name}_schema.json`. It holds no CSVs: the dev seed generates its sample files (`SeedPost16OctoberSamples`).

These row schemas transcribe the field references, SQL types, CSV column letters and supplied headings in `Post16.ods`. `post16-ingress_schema.json` defines one combined document per institution. A collection name identifies the source dataset, so a CSV download can select that collection without guessing from overlapping fields.

Each row schema uses the same extensions:

- `x-ingress.collection` is the key in the combined JSON; `pageTab` groups collections on the page; `institutionKey` identifies the source field used to split incoming records by institution.
- `x-download.fileName` and `properties.*.x-csv.columns` identify the separate CSV and its column order. The variant keys distinguish provisional/revised from retention columns where the workbook does.
- `x-display.section` gives the selector label; `properties.*.x-display` gives the page label, visibility, order and, for names, searchability. Only selected pupil identity fields are marked visible by default. Other fields remain available for a details view.

For example, `students-included`, `students-non-included` and `students-previously-published` all have `pageTab: students`, but occupy separate collections and CSV files. `students-value-added` and `students-aims` also belong to that tab. The screenshot's Subject column has no field in the pupil sheets; it would need a defined join to results and a choice of how to handle multiple results per student.

This is a schema and display contract. The current `CsvSchemaFileProcessor` writes a flat merged array per school and requires `LAESTAB` in every source. To persist this envelope, ingress must tag each dataset and group rows into these collections. The previously published sheet uses `LAESTAB_0` for institution partitioning, and the aims sheet specifies `AimLAESTAB`; those routes need explicit handling. The downloads page must read the collection metadata and produce the workbook's CSV headers/order. The schemas alone do not change those runtime paths.

The workbook does not define JSON nullability or required values consistently across its exercise variants. The row schemas allow null for source fields and leave `required` unset. Fields without a CSV column remain in the schema with an empty `x-csv.columns` map and should not be exported. The included pupil sheet marks six T level core/OS fields removed, and the included results sheet strikes through `SCHCNO` and `EXAMCAND` while reusing their column letters; those fields are excluded from CSV export metadata. The included pupil sheet also has a heading-only inclusion description row without a field reference, so it is not a JSON property.

## Results schema and the dev seed

`results-included_schema.json` carries one field the workbook does not: `SOURCE`. The processor stamps the dataset slot's tag (`16to19_INC` and so on) on every record, and only does so when the schema declares the column, because `additionalProperties` is false. It has no CSV column, so it is not exported. The schema also marks the student and result columns visible, with the names searchable, so the Results tab renders a table.

`results-included_schema.json` and `results-non-included_schema.json` are the 16-18 included and non-included results data files, as their data specifications define them (fields 1-32, headed by field reference; the descriptive "column heading" is the heading of the CSV a school downloads, in column order A-AD). The two differ in the `CAPPED_PTS` download heading ("Maths"/"Math"), so each file has its own schema. The results schemas (these two and `results-late_schema.json`) set **no `maxLength`**: the specifications' lengths were written for SQL tables, and in a CSV import one over-long value would fail validation and stop the whole run. `SCHCNO` and `EXAMCAND` are struck through in the workbook and are not exported. Each schema adds the four values the enquiry journey reads, which are not CSV columns: `x-ingress.source` fills them from the supplier columns — `QAN` from `GNUMBER`, `SYLLABUS` from `BRDSUBNO`, `SESSION` from `SEASON` + `EXAMYEAR` joined (`S2025`), and `QUAL_NAME` from `Short_Qual_Desc` + `SubjectDescription` joined with a space (`GCE A English`). They repeat supplier columns, so they are neither downloaded nor shown on the Results tab.

The "16 to 19 Oct" dev window uses these schemas: the dev seed (`SeedPost16OctoberSamples`) writes that window's sample CSVs to the ingress storage account, container `16-to-19-oct`, and leaves the window for an admin to import. Pair `students/included.csv` with `students-included_schema.json`, `students/nonincluded.csv` with `students-non-included_schema.json`, `results/16to19_INC.csv` with `results-included_schema.json`, `results/16to19_NONINC.csv` with `results-non-included_schema.json`, and `results/16to19_LR1.csv` with `results-late_schema.json`.

## Late results schema and column renaming

`results-late_schema.json` is the 16-19 late results file as the data specification defines it (1618 names): `LAESTAB`, `CYPMD_ID`, `SURNAME`, `FORENAMES`, `AB_CODE_NDAQ`, `Short_Qual_Desc`, `EXAM_YEAR_SEASON`, `EXAM_DATE`, `Discount_Code`, `SYLLABUS_TITLE`, `GNUMBER`, `BRDSUBNO`, `GRADE` and `Late_Result_Type` (`Amendment` or `New`). It is for late results 1. Late results 2 has `results-late-2_schema.json`: the same columns, but its own `x-ingress.collection`, download file and section, because the Results tab keys a dataset by its collection and one schema for both files would merge them into one dataset. The "16 to 19 Nov" dev window (imported and validated by `SeedPost16NovemberSamples`) takes `results/16to19_LR2.csv` from ingress container `16-to-19-nov` with this schema; see `docs/testing-late-results-2.md`. The "16 to 19 Feb" dev window (late results 2 also validated, by `SeedPost16FebruarySamples`) takes the revised files from ingress container `16-to-19-feb`: `results/16to19_INC_REV.csv` with `results-included-revised_schema.json` and `results/16to19_NONINC_REV.csv` with `results-non-included-revised_schema.json`. Those two schemas are copies of the included and non-included results schemas with their own `x-ingress.collection`, download file and section, for the same reason; see `docs/testing-revised-results.md`. The KS4 version, with the KS4 names (`FORENAME`, `QUALIFICATION_TYPE`, `SEASON_AND_YEAR`, `WOLF_DISC_CODE`, `QUALIFICATION_DESCRIPTION`), is `../ks4autumn/results-late_schema.json`.

Ingress writes each JSON key with the property's name. A property may set `x-ingress.source` to the CSV column it is read from — or to a list of columns, joined with `x-ingress.separator` (default none), blank parts skipped — so a supplier column reaches the journey under the name the journey reads: `QAN` ← `GNUMBER`, `QUAL_NAME` ← `SYLLABUS_TITLE` / `QUALIFICATION_DESCRIPTION`, `SYLLABUS` ← `BRDSUBNO`, `SESSION` ← `EXAM_YEAR_SEASON` / `SEASON_AND_YEAR`. It is a copy: a CSV that already has the property's own column keeps it, and the source column is then dropped like any column the schema does not declare.

An `Amendment` row does not replace the result it corrects. Both rows reach the journey, and the amendment carries its late file's tag, so the search shows the original ("File: Included") and the amendment ("File: Late results 1").

## Summary schemas

The 1618 Summary Data sheet feeds four checking exercises over the year, and the supplier sends a different file shape for each. The sheet's three column-letter columns describe those shapes, and its four "Included in" flags say which exercise gets which:

| Exercise | Workbook column | Columns | Schema |
|---|---|---|---|
| Autumn CE | Column (Autumn) | 62 (A–BJ) | `summary-autumn_schema.json` |
| Provisional Value Added (November) | Column (November VA) | 102 (A–CX) | `summary-november-va_schema.json` |
| Light touch, revised data share (February) | Column (November VA) | 102 (A–CX) | `summary-november-va_schema.json` |
| Retention (March) | Column (Retention) | 135 (A–EE) | `summary-retention_schema.json` |

Each schema lists only the fields its file carries, with one `default` column letter each, every field visible and `order` equal to the column position. There is one schema per file shape rather than one schema with three variants, so an exercise's dataset is uploaded with the schema that matches its file and nothing has to record which variant an exercise uses. The workbook's Autumn column repeats `AZ` for `ENTRY_PER_M` and `T_SCOPEEX_E`; the schema numbers the column contiguously, so `T_SCOPEEX_E` onwards is one letter later than the sheet (BA–BJ). All three set `x-display.layout` to `vertical`: one record per school, pivoted into label/value rows. The CSV filename is still to be confirmed; `summary.csv` is a placeholder in all three.
