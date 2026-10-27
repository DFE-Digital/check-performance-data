# Post-16 workbook schemas

These row schemas transcribe the field references, SQL types, CSV column letters and supplied headings in `Post16.ods`. `post16-ingress.json` defines one combined document per institution. A collection name identifies the source dataset, so a CSV download can select that collection without guessing from overlapping fields.

Each row schema uses the same extensions:

- `x-ingress.collection` is the key in the combined JSON; `pageTab` groups collections on the page; `institutionKey` identifies the source field used to split incoming records by institution.
- `x-download.fileName` and `properties.*.x-csv.columns` identify the separate CSV and its column order. The variant keys distinguish provisional/revised from retention columns where the workbook does.
- `x-display.section` gives the selector label; `properties.*.x-display` gives the page label, visibility, order and, for names, searchability. Only selected pupil identity fields are marked visible by default. Other fields remain available for a details view.

For example, `students-included`, `students-non-included` and `students-previously-published` all have `pageTab: students`, but occupy separate collections and CSV files. `students-value-added` and `students-aims` also belong to that tab. The screenshot's Subject column has no field in the pupil sheets; it would need a defined join to results and a choice of how to handle multiple results per student.

This is a schema and display contract. The current `CsvSchemaFileProcessor` writes a flat merged array per school and requires `LAESTAB` in every source. To persist this envelope, ingress must tag each dataset and group rows into these collections. The previously published sheet uses `LAESTAB_0` for institution partitioning, and the aims sheet specifies `AimLAESTAB`; those routes need explicit handling. The downloads page must read the collection metadata and produce the workbook's CSV headers/order. The schemas alone do not change those runtime paths.

The workbook does not define JSON nullability or required values consistently across its exercise variants. The row schemas allow null for source fields and leave `required` unset. Fields without a CSV column remain in the schema with an empty `x-csv.columns` map and should not be exported. The included pupil sheet marks six T level core/OS fields removed, and the included results sheet strikes through `SCHCNO` and `EXAMCAND` while reusing their column letters; those fields are excluded from CSV export metadata. The included pupil sheet also has a heading-only inclusion description row without a field reference, so it is not a JSON property.

## Results schema and the dev seed

`results-included.json` carries one field the workbook does not: `SOURCE`. The processor stamps the dataset slot's tag (`16to19_MAIN` and so on) on every record, and only does so when the schema declares the column, because `additionalProperties` is false. It has no CSV column, so it is not exported. The schema also marks the student and result columns visible, with the names searchable, so the Results tab renders a table; the dev seed's `16to19_MAIN.csv` and `16to19_LR1.csv` are in this shape (the main file and the first late results file), built from the students in `included.csv`. The seed feeds two windows from these files: "16 to 19 ingress" at the start of the Autumn window, and "16 to 19 February" four months in, with pupil data checking shut, the late results file landed and the third Summary exercise enabled.

## Summary schemas

The 1618 Summary Data sheet feeds four checking exercises over the year, and the supplier sends a different file shape for each. The sheet's three column-letter columns describe those shapes, and its four "Included in" flags say which exercise gets which:

| Exercise | Workbook column | Columns | Schema |
|---|---|---|---|
| Autumn CE | Column (Autumn) | 62 (A–BJ) | `summary-autumn.json` |
| Provisional Value Added (November) | Column (November VA) | 102 (A–CX) | `summary-november-va.json` |
| Light touch, revised data share (February) | Column (November VA) | 102 (A–CX) | `summary-november-va.json` |
| Retention (March) | Column (Retention) | 135 (A–EE) | `summary-retention.json` |

Each schema lists only the fields its file carries, with one `default` column letter each, every field visible and `order` equal to the column position. There is one schema per file shape rather than one schema with three variants, so an exercise's dataset is uploaded with the schema that matches its file and nothing has to record which variant an exercise uses. The workbook's Autumn column repeats `AZ` for `ENTRY_PER_M` and `T_SCOPEEX_E`; the schema numbers the column contiguously, so `T_SCOPEEX_E` onwards is one letter later than the sheet (BA–BJ). All three set `x-display.layout` to `vertical`: one record per school, pivoted into label/value rows. The CSV filename is still to be confirmed; `summary.csv` is a placeholder in all three.
