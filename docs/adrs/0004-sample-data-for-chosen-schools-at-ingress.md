# Sample data for chosen schools is made at ingress

Date: 2026-11-25

Status: Accepted

## Context

User research runs in production with an extract of real supplier data. Some schools ask us not
to show their real data, and to show sample data instead.

- Nobody may download the supplier CSV. It holds real pupil data.
- The supplier does not know which schools opt out before it sends the file.
- Each clean ingress run is a release under a new prefix. A file that someone writes into storage
  by hand is not part of a release, and the next run does not keep it.

So the service itself must replace the data of those schools, and it must do it in each run.

## Decision

- Each window has a list of **sample schools** (`CheckingWindowSampleSchools`: LAESTAB, URN).
  Admins change it on the window page. The generic audit capture records each change.
- At ingress, the processor reads the CSV rows, then removes every row of a sample school. It adds
  generated rows for each sample school, also for a school that is not in the file. The generated
  rows then go through the same steps as supplier rows: type conversion, the inclusion stamps and
  schema validation.
- A schema names the generator of its rows with `"x-ingress": { "sample": "<name>" }`. Code does not
  find a generator from a dataset name or a key stage.
- **Fail closed.** When the window has a sample school, and a schema names no generator or names one
  that does not exist, the run fails by name. Supplier rows of a sample school never pass through.
- KS2 has generators that make full, consistent data. KS4 and 16-19 use one generator that reads
  the file's schema. It fills identifiers, names, dates, inclusion codes and results, and leaves
  most measures blank or small.
- A generator gets every value from the school's LAESTAB and URN. The same school gets the same
  pupils and UPNs in each release.
- Each release records the LAESTABs it gave sample data (`CheckingExerciseRelease.SampleLaestabs`).
  The admin pages warn when the window's list differs from the live release.
- A ticket for a request made against a release that gave the school sample data gets the Zendesk
  tag `cpd_sample_data` and the subject prefix `[SAMPLE DATA] `.

## Consequences

- A change to the list has an effect only after each exercise of the window validates again.
- The real rows of a sample school stay in the supplier file in the ingress container. A release
  made before the school opted out keeps its real data in storage. Schools do not see it.
- Each kind of supplier file needs a generator before a window with sample schools can ingest it.
- The generators are product code with tests. The KS2 dev seed data calls the same code.
- The KS4 and 16-19 sample data is less complete than KS2. A specific generator can replace the
  generic one later: change the name in the schema.
