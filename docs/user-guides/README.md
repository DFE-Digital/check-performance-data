# User guides

| Guide | Source | Output | Audience |
|---|---|---|---|
| Admin user guide | `admin-user-guide.md` | `admin-user-guide.pdf` | DfE staff who use the admin area |

## Rebuilding the admin user guide

The Markdown file is the source of truth. Edit it, recapture any screenshot whose
screen changed, then rebuild the PDF and commit all three.

Requirements: Node 20+, Docker Desktop, and the Playwright Chromium build the E2E
suite already installs (`pwsh tests/DfE.CheckPerformanceData.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium`
if `npm run capture` reports a missing browser).

```powershell
docker compose up -d --build              # from the repo root; wait for http://localhost:8080/healthcheck
cd docs/user-guides/admin/tools
npm install
npm run capture                           # all screenshots, or: npm run capture -- dashboard audit-log
npm run build                             # writes ../../admin-user-guide.pdf
```

`capture.mjs` signs in through `/dev/impersonate/admin`, which exists only where dev
tools are enabled (the compose stack). Never run it against a deployed environment.
Seed data first so screens are not empty: open `/dev/queues/seed-dlq`, run
"Seed sample search data" from the admin Test data group, and seed or run one egress.
The E2E seed helpers in `tests/DfE.CheckPerformanceData.E2ETests/Helpers/SeedHelpers.cs`
show the exact calls.
