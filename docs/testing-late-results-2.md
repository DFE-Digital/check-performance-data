# Testing guide: add late results 2 to the "16 to 19 Nov" window

This guide tests the November step of the 16-19 results enquiry: an admin adds the second late
results file (`16to19_LR2`) to a results enquiry that already has a validated October release.

## What the seed gives you

The dev seed makes the window **"16 to 19 Nov"** (`DevDataSeeder.Post16NovemberCheckingWindowId`,
`3b1d7c52-6e0a-4f8b-9c21-5a4e8d7f1b63`). It has the same exercises, slots and dates as
"16 to 19 Oct". The difference: the seed has already imported and validated the October files
(`SeedPost16NovemberSamples`).

| Exercise | Slot | File | Schema | State after seed |
|---|---|---|---|---|
| Pupil data checking | Students: included | `included.csv` | `students-included_schema.json` | Validated |
| Pupil data checking | Students: non-included | `nonincluded.csv` | `students-non-included_schema.json` | Validated |
| Results enquiry | Included | `16to19_INC.csv` | `results-included_schema.json` | Validated |
| Results enquiry | Non-included | `16to19_NONINC.csv` | `results-non-included_schema.json` | Validated |
| Results enquiry | Late results 1 | `16to19_LR1.csv` | `results-late_schema.json` | Validated |
| Results enquiry | Late results 2 | — | — | **Not supplied** |

The seed also writes the late results 2 sample to the ingress storage account:

- Container `16-to-19-nov`, file `results/16to19_LR2.csv`.
- Its schema is `src/DfE.CheckPerformanceData.Web/Data/Ingress/post16/results-late-2_schema.json`.
  You upload it from your machine.

Use `results-late-2_schema.json`, not `results-late_schema.json`. The two schemas have the same
columns, but a different collection. The Results tab keys a dataset by its collection, so one schema
for both files puts late results 1 and late results 2 in one section.

### The late results 2 rows

All rows are for Kingsmead School (`860/4070`), the school the dev impersonation signs in as.

| CYPMD ID | Student | Qualification | Grade | Type | Why |
|---|---|---|---|---|---|
| `500001` | Alice Smith | GCSE English Language (`60148366`), S2024 | 7 | Amendment | Late results 1 gave 6 |
| `500003` | Charlie Smith | GCSE Mathematics (`60146084`), S2024 | 3 | Amendment | The included file gave 2 |
| `500002` | Bob Smith | GCE A Level Art and Design (`60149589`), S2024 | C | New | Bob had no Art result |
| `500005` | Edward Smith | OCR FSMQ Additional Maths (`10025480`), S2024 | B | New | Edward had **no** result before |
| `500202` | Bob Johnson (non-included) | BTEC Sport (`60172186`), S2024 | M | New | Bob Johnson had **no** result before |

An Amendment row does not replace the earlier row. After the run, both rows show.

## Before you start

1. Start the stack and seed:

   ```sh
   docker compose --profile web --profile database --profile storage up -d --build
   ```

   Or run the web app with `dotnet run --project src/DfE.CheckPerformanceData.Web --launch-profile http`.
   The `http` profile sets `Dev__ToolsEnabled=true`, which the impersonation routes need.
2. If the database already has windows from an earlier seed, use **Reset seed data** on the admin
   danger zone page. The seed deletes and makes all dev windows again.
3. Sign in as an admin: `/dev/impersonate/admin`. For school pages, use `/dev/impersonate/editor`.
   Do not open `/LandingPage` while you impersonate. It starts a real DfE Sign-in and drops the
   impersonation cookie.

## Test 1: the seeded state is correct

1. As an admin, open `/admin/windows/summary/3b1d7c52-6e0a-4f8b-9c21-5a4e8d7f1b63`.
2. Make sure that both exercises show the **Validated** tag.
3. In the Results enquiry data files table, make sure that:
   - Included, Non-included and Late results 1 show their file and schema names.
   - Late results 2 shows **Not supplied** and **(optional)**.
4. As an editor, open `/CheckYourPupilData/3b1d7c52-6e0a-4f8b-9c21-5a4e8d7f1b63`.
5. Make sure that the Students tab shows 240 students (120 included, 120 non-included).
6. Open the Results tab. Make sure that it shows the Included, Non-included and Late results
   sections, and no Late results 2 section.

## Test 2: the late results guidance shows before late results 2

1. As an editor, on the Check your pupil data page, choose the results enquiry option and continue.
2. Choose **Incorrect grade**.
3. Make sure that the page **"Check your second late results file before you report an incorrect
   grade"** shows.
4. Search for a student. Make sure that **Edward Smith** is **not** in the suggestions. He holds no
   result yet, and the search lists only students with results.
5. Cancel the journey.

## Test 3: add late results 2

1. As an admin, open the window summary. Choose **Edit** on the Results enquiry to open its edit
   page (`/admin/windows/{id}/exercises/{exerciseId}/edit`).
2. In **Data files**, on the Late results 2 row, choose **Choose CSV**.
3. Open the `16-to-19-nov` container, then `results/`. Choose `16to19_LR2.csv`.
4. On the same row, choose **Choose schema**. Upload `results-late-2_schema.json`.
5. Go back to the window summary. Make sure that:
   - Late results 2 shows `16to19_LR2.csv` and `results-late-2_schema.json`.
   - The Results enquiry shows **Validated: Not since the files changed**.
   - Pupil data checking is still **Validated**. A change to one exercise does not touch the other.
6. Choose **Validate** on the Results enquiry. Wait for the run to complete without errors.
7. Make sure that the Results enquiry shows the **Validated** tag again.
8. On the exercise edit page, in **Data releases**, make sure that there is a new release and that
   it is the live release. The October release is still in the list.

## Test 4: schools see late results 2

1. As an editor, open `/CheckYourPupilData/3b1d7c52-6e0a-4f8b-9c21-5a4e8d7f1b63`.
2. Open the Results tab. Make sure that there is a **Late results 2** section with 5 rows.
3. Make sure that the Late results 1 section still shows its rows. Late results 2 does not replace it.
4. Start the results enquiry and choose **Incorrect grade**.
5. Make sure that the late results guidance page does **not** show. The journey goes straight to
   **"Does the incorrect grade affect the whole cohort?"**.
6. Search for **Edward Smith**. Make sure that he is now in the suggestions.
7. Choose Edward Smith. Make sure that his result reads
   *"OCR Level 3 FSMQ: Additional Maths, QAN: 10025480, Session: S2024, File: Late results 2"*.
8. Go back and choose **Alice Smith**. Make sure that GCSE English Language shows twice:
   grade 6 (**File: Late results 1**) and grade 7 (**File: Late results 2**).
9. Choose **Charlie Smith**. Make sure that GCSE Mathematics shows grade 2 (**File: Included**) and
   grade 3 (**File: Late results 2**).
10. Search for **Bob Johnson** (non-included). Make sure that he is in the suggestions, with the
    BTEC Sport result from late results 2.

## Test 5: go back to the October release (optional)

1. As an admin, on the Results enquiry edit page, in **Data releases**, choose **Make live** on the
   October release.
2. As an editor, make sure that the Results tab has no Late results 2 section, that Edward Smith is
   not in the search, and that the late results guidance shows again.
3. Make the November release live again.

## Automated cover

- `SeededCheckingExerciseTests.The_November_window_has_the_October_files_validated_and_takes_late_results_2`
  runs the seed, then adds the LR2 file and schema, validates, and checks the new release, the
  results and the late results guidance.
- `SeedPost16NovemberSamplesTests` pins the LR2 sample: its columns, its Amendment/New marks, its
  students, and that its schema is a separate dataset from late results 1.
