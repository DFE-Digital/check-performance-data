# Post-16 workbook schemas

These eight row schemas transcribe the field references, SQL types, CSV column letters and supplied headings in `Post16.ods`. `post16-ingress.json` defines one combined document per institution. A collection name identifies the source dataset, so a CSV download can select that collection without guessing from overlapping fields.

Each row schema uses the same extensions:

- `x-ingress.collection` is the key in the combined JSON; `pageTab` groups collections on the page; `institutionKey` identifies the source field used to split incoming records by institution.
- `x-download.fileName` and `properties.*.x-csv.columns` identify the separate CSV and its column order. The variant keys distinguish provisional/revised from retention columns where the workbook does.
- `x-display.section` gives the selector label; `properties.*.x-display` gives the page label, visibility, order and, for names, searchability. Only selected pupil identity fields are marked visible by default. Other fields remain available for a details view.

For example, `students-included`, `students-non-included` and `students-previously-published` all have `pageTab: students`, but occupy separate collections and CSV files. `students-value-added` and `students-aims` also belong to that tab. The screenshot's Subject column has no field in the pupil sheets; it would need a defined join to results and a choice of how to handle multiple results per student.

This is a schema and display contract. The current `CsvSchemaFileProcessor` writes a flat merged array per school and requires `LAESTAB` in every source. To persist this envelope, ingress must tag each dataset and group rows into these collections. The previously published sheet uses `LAESTAB_0` for institution partitioning, and the aims sheet specifies `AimLAESTAB`; those routes need explicit handling. The downloads page must read the collection metadata and produce the workbook's CSV headers/order. The schemas alone do not change those runtime paths.

The workbook does not define JSON nullability or required values consistently across its exercise variants. The row schemas allow null for source fields and leave `required` unset. Fields without a CSV column remain in the schema with an empty `x-csv.columns` map and should not be exported. The included pupil sheet marks six T level core/OS fields removed, and the included results sheet strikes through `SCHCNO` and `EXAMCAND` while reusing their column letters; those fields are excluded from CSV export metadata. The included pupil sheet also has a heading-only inclusion description row without a field reference, so it is not a JSON property. The summary tab leaves its CSV filename to be confirmed; `summary.csv` is a placeholder.
