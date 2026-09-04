# How question flow configs reach an environment

Journey question flows are JSON files in `src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/`,
named `{WhatToChange}_{CheckingWindowType}.json`. They are **served from the release image** in
every environment, by `FileSystemQuestionFlowClient`, reading
`{ContentRootPath}/Data/QuestionFlows/`.

Nothing else is needed. The Web SDK's default content glob includes `**/*.json`, so
`dotnet publish` already places them at `/app/Data/QuestionFlows/` in the container. A flow change
therefore reaches an environment by the ordinary act of deploying the commit that contains it.

## Why not blob storage

They used to be read from the `question-flows` blob container. That container was provisioned
**empty** by Terraform (`terraform/application/storage.tf`), and the only thing that ever filled it
was a seeding step gated on `IsDevelopment()` or `SeedDevelopmentData=true` — set only in
`review.yml` and `development.yml`. QA, preproduction and production therefore held whatever
somebody had uploaded by hand, if anything, and **a config change did not reach an environment just
by deploying it**. The storage account also sets `public_network_access_enabled = false`, so a
pipeline runner could not upload into it either.

That drift was load-bearing in a bad way: it is the stated reason journey date rules
(`Application/Journey/DateRules/`) are written in C# rather than declared in the flow JSON.

## What this costs

A flow cannot be hotfixed without a redeploy. Nothing edits these at runtime — there is no admin
flow editor, and the interface (`IQuestionFlowConfigSource`) is read-only — so today that costs
nothing.

## If a storage-backed source is ever wanted again

Wanting one means wanting business users to edit flows without a deploy. In that case do **not**
reinstate a plain seed-if-missing upload: it freezes each environment at whatever landed first,
which is the original bug. Copy the version gate from
`Infrastructure/RulesEngine/RulesConfigSeeder.cs` — a `version` field in each config, the bundled
copy replacing the stored one only when it is strictly newer, admin saves stamped so they outrank
the bundled seed. `GradeReferenceSeedingService` is the simpler precedent for a hosted service that
runs in every environment.

The `question-flows` container has been removed from `terraform/application/storage.tf`. Terraform
deletes the container on the next apply in each environment, along with any configs that had been
uploaded to it by hand — that content is superseded by the files in the image, but it is gone, not
archived. Recreating the container means re-adding it to the `containers` list; the blobs would have
to be re-uploaded from `Web/Data/QuestionFlows/`.
