# Rules engine seed configuration

This folder holds the seed configuration the rules engine reads from blob storage:

| File | Used by | Updated by |
|---|---|---|
| `rules.json` | `RulesEngine.Evaluate(...)` | Business users (post-deploy) |
| `country-languages.json` | `OfficialLanguageIs` predicate lookup | Business users (post-deploy) |

The schema is defined by `DfE.CheckPerformanceData.Application.RulesEngine` (`RuleSet`, `Predicate`, `FieldCatalogue`). The unit test project's `SeedRulesValidationTests` pins both files to the schema — break the schema and the build fails.

## Local development

`docker-compose --profile all up` brings up Azurite and the `azurite_init` one-shot job that uploads both JSON files into the `rules-config` container. The worker waits for that upload before starting. To push a manual rule change while the stack is running:

```sh
az storage blob upload \
  --connection-string "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;" \
  --container-name rules-config \
  --name rules.json \
  --file ./src/DfE.CheckPerformanceData.RulesEngineWorker/seed/rules.json \
  --overwrite
```

The worker picks the change up within `RulesEngineOptions:RefreshIntervalSeconds` (default 300s).

## Deploying to a real environment

The Terraform `module.storage` block in `terraform/application/storage.tf` provisions the `rules-config` container, but **does not** upload these JSON files. Seeding happens once per environment via the helper script:

```sh
./scripts/seed-rules-config.sh <storage-account-name> <resource-group>
```

The script is idempotent — re-running it just overwrites the blobs (which the provider treats as a normal refresh via the ETag swap).

The eventual goal is to fold this seed upload into the CI deploy pipeline (`.github/workflows/build-and-deploy.yml`). That work is intentionally **not** included here because it needs an Azure-side decision on:
1. Whose identity uploads the seed (workflow OIDC or a service principal).
2. The exact storage-account name pattern emitted by `module.storage` (depends on `azure_resource_prefix` + `service_short` conventions in the AKS module).

## Editing the rules

`rules.json` is processed by `RuleSetValidator` on every refresh. A broken file is logged and rejected; the worker keeps serving the previous good copy. The validator enforces:

- Every outcome's last branch is `"otherwise"`.
- Every `field` reference exists in `FieldCatalogue` and matches its declared type.
- Date literals are an ISO `yyyy-MM-dd` string or a window-relative object (below).
- Branch IDs are unique within an outcome.

### Window-relative dates

A date literal may be a window-relative object instead of a fixed date. An absolute date stays an ISO `yyyy-MM-dd` string. The window year is the calendar year of the window's PupilData exercise start date. The engine resolves the date from the window the request belongs to. It never uses the current date.

| Token | Parameters | Resolves to |
|---|---|---|
| `dayMonthOfWindowYear` | `month` 1–12, `day` (valid in a leap year) | the named day-month of the window year |
| `firstSeptemberMinusYears` | `years` 0–99 | 1 September of the window year, minus `years` |
| `secondMondayOfMay` | none | the second Monday of May of the window year |

JSON shape:

```json
{ "field": "dateOfRemoval", "gt": { "windowRelative": "dayMonthOfWindowYear", "month": 1, "day": 16 } }
{ "field": "schoolAdmissionDate", "gte": { "windowRelative": "firstSeptemberMinusYears", "years": 3 } }
{ "field": "dateOfRemoval", "gte": { "windowRelative": "secondMondayOfMay" } }
```

#### Which window year the shipped dates match

The shipped `rules.json` was converted from the fixed dates of two different windows:

- The **KS4** branches (window type `KS4June`) were written for the **2026** window:
  `EAL-ACC-NEWADM-2022-*` resolve to 1 September 2022 — 1 September four years before the
  window year, the same boundary the KS4 journey validates as `cohort.start` — and
  `EAL-ACC-ADDBACK-2021-*` to 1 September 2021, five years back. `EHE-KS4` / `PEX-KS4`
  resolve to 16 January of the window year (16 January 2026); their fixed date of 16 January
  2025 rolled forward to the window year at conversion, by agreement.
- The **KS2** branches were written for the **2027** window: the EAL split at
  1 September 2024, `AFE-KS2` at 1 September 2025, the May rules at 10 May 2027, and
  `PAS-REJ` at 1 April 2027.

In any other window year the dates move one year per year of window: that is the point of
the conversion. `Ks4RemovalRulesTests` and `Ks2RemovalRulesTests` pin each converted branch
against its original fixed date for the window it was written for.

The window year is the calendar year of the window's PupilData exercise start date. The
tables above assume a real window's PupilData exercise starts in the same calendar year the
branches were written for (KS4 June 2026, KS2 2027). If a real window's PupilData exercise
starts in a different year, every boundary moves with it — no error is raised. Before a real
window opens, check the per-window rules page (`/admin/windows/{id}/rules`) against the
expected boundaries above.

The year in a branch ID such as `EAL-ACC-NEWADM-2022-ALL` is historical: it is the year the
date named when the branch was first written with a fixed date, not a year the branch reads
at runtime. Branch IDs appear in the decision trace and in Zendesk tickets, so they are not
renamed when a date becomes window-relative.

If a date cannot resolve for a window, the rule matches nothing and the request goes to manual handling. This happens when 29 February falls in a non-leap window year.

If you change a field name or add a new field, also update:

- `Application/RulesEngine/FieldCatalogue.cs` (declares the field + type).
- `Application/RulesEngine/AnswerFieldMap.cs` (maps the producer's `QuestionId` to the canonical name).
- `tests/.../SeedRulesValidationTests.cs` (if you add a new outcome key).
