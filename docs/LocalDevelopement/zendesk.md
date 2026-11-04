# Zendesk in local development

The rules engine worker (`RulesEngineWorker`) sends each request to Zendesk as a ticket. On your
machine it can use one of two clients:

- **The fake client.** The worker writes each ticket to the `DevZendeskTickets` table. Nothing
  goes to Zendesk. Use this for most local work.
- **The real client.** The worker sends real tickets to preprod Zendesk (`esfa-preprod`). People
  in the business read these tickets, and the HS checking exercise report counts them.

`Zendesk:UseFake` selects the client. The worker's `appsettings.json` sets it to `false`, so the
worker uses the real client when nothing else sets it.

## Use the fake client

Set `Zendesk__UseFake=true` on the worker.

- **Docker Compose:** add `- Zendesk__UseFake=true` to the `environment` list of the
  `rules_engine` service in `docker-compose.yaml`, or to your own override file.
- **On the host:** set `"Zendesk": { "UseFake": true }` in your
  `RulesEngineWorker/appsettings.Development.json`, or set the `Zendesk__UseFake=true`
  environment variable.

Then open `/dev/zendesk/outbox` in the web app to see the tickets that the worker made.

## Send tickets to preprod Zendesk

Do this only when you must test a ticket in Zendesk. Before you start the worker:

1. Set `Zendesk__UseFake=false`.
2. Set `ZendeskSettings` (`Subdomain`, `Domain`, `ClientId`, `ClientSecret`) in your
   `RulesEngineWorker/appsettings.Development.json`. Git ignores this file. Do not commit the
   client secret.
3. Do not set `ZendeskTicketFields` in your own settings. The worker's `appsettings.json` holds the
   preprod field IDs, and git tracks it. A value in your own file overrides the tracked value.
4. Start the worker and read its startup log. Look for these two warnings:
   - "Zendesk credentials are not configured". A `ZendeskSettings` value is missing.
   - "Zendesk ticket field IDs are not configured". It names each `ZendeskTicketFields__*Id`
     setting that is not set or is 0. The worker leaves those fields out of each ticket.

   If you see a warning, stop the worker and fix the settings.
5. Read each test ticket back from Zendesk. Close the test tickets when you finish.

## Where the field IDs come from

| Where the worker runs | Field IDs |
| --- | --- |
| Your machine | `RulesEngineWorker/appsettings.json` (the preprod IDs) |
| Deployed environments | `ZendeskTicketFields__*` in `terraform/application/config/*.yml`. These environment variables override `appsettings.json`. |

The development, review, QA and preprod environments all use the preprod Zendesk, so they use the
same IDs. Production uses 0 for each ID until its IDs are confirmed. If you add a ticket field,
add its ID to `appsettings.json` and to each file in `terraform/application/config/`.

The field IDs are not secrets.
