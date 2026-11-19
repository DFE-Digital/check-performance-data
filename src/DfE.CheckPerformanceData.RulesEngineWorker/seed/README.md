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

A date literal may be a window-relative object instead of a fixed date. An absolute date stays an ISO `yyyy-MM-dd` string. The window year is the calendar year of the checking window start date. The engine resolves the date from the window the request belongs to. It never uses the current date.

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

If a date cannot resolve for a window, the rule matches nothing and the request goes to manual handling. This happens when 29 February falls in a non-leap window year.

If you change a field name or add a new field, also update:

- `Application/RulesEngine/FieldCatalogue.cs` (declares the field + type).
- `Application/RulesEngine/AnswerFieldMap.cs` (maps the producer's `QuestionId` to the canonical name).
- `tests/.../SeedRulesValidationTests.cs` (if you add a new outcome key).
