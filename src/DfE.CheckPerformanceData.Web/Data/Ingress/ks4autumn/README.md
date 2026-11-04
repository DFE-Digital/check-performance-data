# KS4 Autumn schemas

This folder holds the KS4 Autumn supplier (LDS) schemas, named `{name}_schema.json`. Each one transcribes a tab of the LDS Data Spec workbook. It holds no CSVs: the dev seed (`SeedKs4AutumnSamples`) generates the sample files.

| Schema | Spec tab | Exercise and slot |
|---|---|---|
| `pupils_schema.json` | KS4 Pupil Autumn Data (Restruc) | Pupil data checking, `pupils` |
| `results_schema.json` | KS4 Results Data (Restructured) | Results enquiry, `KS4_MAIN` |
| `results-late_schema.json` | KS4&1618 Late Results (KS4 names) | Results enquiry, `KS4_LR1` |
| `summary_schema.json` | KS4 Summary Data report | "Summary" data share, `summary` |

The schemas read the spec as the other supplier schemas do (see `../post16/README.md`):

- The **field reference** is the CSV header. Ingress matches headers to property names with case, so a property has the spec's own spelling: `Sex`, not `SEX`. The read models are not case-sensitive.
- The **column heading** is the heading of the CSV a school downloads (`x-csv.heading`), in the spec's column order (`x-csv.columns`).
- **Lengths and SQL types** were written for SQL tables. The schemas set no `maxLength`, because one long value would stop the whole ingress run.
- A field the spec marks **removed (2026)** (the EBacc fields) has no column and is left out. The LDS ingress file has every field. `MATCHREF` is not in the school's download, so it has an empty `x-csv.columns`.

## Pupils

`P_INCL` decides inclusion through `x-ingress.inclusion`. The included codes are the spec's annex: 401, 403, 414, 421 and 431. The descriptions also name the not-included codes, which add 412 and 425 to the KS4 June list. Every pupil who is not included is includable. `P_INCL_DESC` has no field reference in the spec; the name is the one the KS4 June file uses.

The spec gives dates (`DOB`, `ENTRYDAT`) as DD/MM/YYYY. The schema sets no date pattern, so YYYY-MM-DD is also read. The page shows both as DD/MM/YYYY.

## Results

`results_schema.json` adds the values the enquiry journey reads, which are not CSV columns. `x-ingress.source` fills them from the supplier's columns: `QAN` from `GNUMBER`, `QUAL_NAME` from `QUALIFICATION_DESCRIPTION`, `SYLLABUS` from `BRDSUBNO` and `SESSION` from `SEASON_AND_YEAR`. The slot stamps `SOURCE`. The `ACADEMIC` heading is spelt "qualifcation", as in the spec.

## Summary

The fields are the state schools report, in its order, with `LAESTAB` first. The reports for independent schools, FE colleges and non-maintained special schools are subsets, and each field's description names the reports it is in. The label schools see is the spec's "Summary Report CE website description". Values are strings, because a suppressed value is sent as `NA` or `+`.
