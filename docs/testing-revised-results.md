# Testing guide: add the revised results to the "16 to 19 Feb" window

This guide tests the February step of the 16-19 results enquiry. An admin adds the two revised
results files (`16to19_INC_REV` and `16to19_NONINC_REV`) to a results enquiry that already has an
October and a November release. The revised files **replace** the included, non-included and both
late results files, so the admin also retires those four slots.

## What the seed gives you

The dev seed makes the window **"16 to 19 Feb"** (`DevDataSeeder.Post16FebruaryCheckingWindowId`,
`9e2a4c71-3b5d-4f60-8a17-c4d2e6f80b95`). It has the same exercises, slots and dates as
"16 to 19 Nov". The difference: the seed has also added and validated late results 2
(`SeedPost16FebruarySamples`). The results enquiry has two releases: October, and November (live).

| Exercise | Slot | File | Schema | State after seed |
|---|---|---|---|---|
| Pupil data checking | Students: included | `included.csv` | `students-included_schema.json` | Validated |
| Pupil data checking | Students: non-included | `nonincluded.csv` | `students-non-included_schema.json` | Validated |
| Results enquiry | Included | `16to19_INC.csv` | `results-included_schema.json` | Validated |
| Results enquiry | Non-included | `16to19_NONINC.csv` | `results-non-included_schema.json` | Validated |
| Results enquiry | Late results 1 | `16to19_LR1.csv` | `results-late_schema.json` | Validated |
| Results enquiry | Late results 2 | `16to19_LR2.csv` | `results-late-2_schema.json` | Validated |
| Results enquiry | Included revised | — | — | **Not supplied** |
| Results enquiry | Non-included revised | — | — | **Not supplied** |

The seed also writes the revised samples to the ingress storage account, container `16-to-19-feb`:

| File | Schema (upload from your machine) | Rows |
|---|---|---|
| `results/16to19_INC_REV.csv` | `results-included-revised_schema.json` | 62 |
| `results/16to19_NONINC_REV.csv` | `results-non-included-revised_schema.json` | 33 |

The schemas are in `src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/`. They have the same
columns as `results-included_schema.json` and `results-non-included_schema.json`, but their own
collection, download file and section, so each revised file is its own section on the Results tab.

### What the revised files hold

The revised files hold one row for each result in the four earlier files (98 rows become 95). Where
a late file amended a result, the revised file holds only the amended grade. A student is in the
included revised file when the student is included, and in the non-included revised file when not.

| CYPMD ID | Student | Qualification | November release | Revised |
|---|---|---|---|---|
| `500001` | Alice Smith | GCSE English Language (`60148366`), S2024 | 6 (Late results 1) and 7 (Late results 2) | 7 |
| `500003` | Charlie Smith | GCSE Mathematics (`60146084`), S2024 | 2 (Included) and 3 (Late results 2) | 3 |
| `500002` | Bob Smith | BTEC Sport (`60172186`), S2024 | M (Included) and D (Late results 1) | D |
| `500001` | Alice Smith | GCE A Level Art and Design (`60149589`), S2024 | A (Included) | **B** — changed only in the revised file |
| `500005` | Edward Smith | OCR FSMQ Additional Maths (`10025480`), S2024 | B (Late results 2) | B |
| `500202` | Bob Johnson (non-included) | BTEC Sport (`60172186`), S2024 | M (Late results 2) | M, in the non-included revised file |

Alice Smith's two GCSE Mathematics results (S2023 and S2024) stay two rows: the sessions differ.

## Before you start

1. Start the stack and seed:

   ```sh
   docker compose --profile web --profile database --profile storage up -d --build
   ```

   Or run the web app with `dotnet run --project src/DfE.CheckPerformanceData.Web --launch-profile http`.
2. If the database already has windows from an earlier seed, use **Reset seed data** on the admin
   danger zone page. The seed deletes and makes all dev windows again.
3. Sign in as an admin: `/dev/impersonate/admin`. For school pages, use `/dev/impersonate/editor`.
   Do not open `/LandingPage` while you impersonate.

## Test 1: the seeded state is correct

1. As an admin, open `/admin/windows/summary/9e2a4c71-3b5d-4f60-8a17-c4d2e6f80b95`.
2. Make sure that both exercises show the **Validated** tag.
3. In the Results enquiry data files table, make sure that Included, Non-included, Late results 1
   and Late results 2 show their file and schema names, and that Included revised and Non-included
   revised show **Not supplied** and **(optional)**.
4. Open the Results enquiry edit page, **Data** tab. Make sure that the **Status** column shows
   **Live** for Included, Non-included, Late results 1 and Late results 2, and **Not supplied** for
   the three revised slots (Included revised, Non-included revised, Included revised with retention).
5. In **Data releases**, make sure that there are two releases and that the second (November) is live.
6. As an editor, open `/CheckYourPupilData/9e2a4c71-3b5d-4f60-8a17-c4d2e6f80b95`.
7. Open the Results tab. Make sure that it shows the Included, Non-included, Late results and
   Late results 2 sections.

## Test 2: add the revised files

Add the files **before** you retire the old slots. **Validate data** is not available while no slot
in use holds a file.

1. As an admin, open the window summary. Choose **Edit** on the Results enquiry, then the **Data** tab.
2. On the Included revised row, choose **Choose CSV**. Open the `16-to-19-feb` container, then
   `results/`. Choose `16to19_INC_REV.csv`.
3. On the same row, choose **Choose schema**. Upload `results-included-revised_schema.json`.
4. On the Non-included revised row, choose `16to19_NONINC_REV.csv` and upload
   `results-non-included-revised_schema.json`.
5. Make sure that both revised rows show **Not validated**. Schools do not see them yet.
6. Make sure that the validation line reads **Files have changed since the last validation**.

## Test 3: retire the four replaced files

1. On the Included row, in the **In use** column, choose **Retire**.
2. Make sure that the confirmation page says that the next validation makes a release without the
   file, and that schools keep the live release until then. Choose **Retire data file**.
3. Make sure that the Included row now shows the grey **Retired** tag, where it showed **Live**, and
   **Put back in use**.
4. Do the same for Non-included, Late results 1 and Late results 2.
5. Make sure that each of the four rows says **Retired** under its name, where it said **Required**
   or **Optional** before.
6. As an editor, open the Results tab. Make sure that it still shows the November sections.
   Retiring does not change the live release.
7. As an admin, open the window summary. Make sure that the Results enquiry data files table shows
   only Included revised and Non-included revised. The summary does not list retired slots.
8. Make sure that Pupil data checking is still **Validated**.

## Test 4: validate the February release

1. On the Results enquiry Data tab, choose **Validate data**. Wait for the run to complete without
   errors.
2. Make sure that the validation line reads **Up to date**.
3. Make sure that both revised rows now show **Live**, the four retired rows still show **Retired**,
   and Included revised with retention still shows **Not supplied**.
4. In **Data releases**, make sure that there is a third release and that it is live. The October
   and November releases are still in the list.

## Test 5: schools see only the revised results

1. As an editor, open `/CheckYourPupilData/9e2a4c71-3b5d-4f60-8a17-c4d2e6f80b95`.
2. Open the Results tab. Make sure that it shows **Results included revised** (62 rows) and
   **Results non-included revised** (33 rows), and no Included, Non-included, Late results or
   Late results 2 section.
3. Start the results enquiry and choose **Incorrect grade**.
4. Make sure that the late results guidance page does **not** show. The journey goes straight to
   **"Does the incorrect grade affect the whole cohort?"**.
5. Search for **Alice Smith**. Choose her. Make sure that:
   - GCSE English Language shows **once**, grade 7, **File: Included revised**.
   - GCE A Level Art and Design shows grade **B**.
   - GCSE Mathematics shows twice, S2023 (grade 4) and S2024 (grade 5).
6. Go back and choose **Charlie Smith**. Make sure that GCSE Mathematics shows once, grade 3.
7. Choose **Bob Smith**. Make sure that BTEC Sport shows once, grade D.
8. Choose **Edward Smith**. Make sure that he is still in the suggestions, with the FSMQ Additional
   Maths result, **File: Included revised**.
9. Search for **Bob Johnson** (non-included). Make sure that his BTEC Sport result shows
   **File: Non-included revised**.

## Test 6: go back to the November release (optional)

1. As an admin, on the Results enquiry edit page, in **Data releases**, choose **Make live** on the
   November release.
2. As an editor, make sure that the Results tab shows the four November sections again, and that
   Alice Smith's English Language shows twice (grades 6 and 7).
3. Make the February release live again.

## Test 7: put a slot back in use (optional)

1. As an admin, on the Data tab, choose **Put back in use** on Late results 2 and confirm.
2. Make sure that Late results 2 shows **Not validated**: the live February release did not read it.
3. Make sure that the validation line reads **Files have changed since the last validation**.
4. Retire Late results 2 again. Do not validate with it back in use: its rows would show beside the
   revised rows that already hold them.

## Automated cover

- `SeededCheckingExerciseTests.The_February_window_has_late_results_2_validated_and_takes_the_revised_files`
  runs the seed, checks the two releases, then adds the revised files, retires the four slots,
  validates, and checks that the new release holds only the revised rows.
- `SeedPost16FebruarySamplesTests` pins the revised samples: their columns, their schemas, one row
  per earlier result, the amended grades, and that each student is in the file for their inclusion.
