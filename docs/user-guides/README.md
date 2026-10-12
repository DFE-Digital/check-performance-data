# User guides

| Guide | Source | Output | Audience |
|---|---|---|---|
| Admin user guide | `admin-user-guide.md` | `admin-user-guide.pdf` | DfE staff who use the admin area |
| How to use the CMS | `cms/pages/*.md` | Pages under `/help/how-to-use-the-cms` in the service itself | Content editors and administrators |

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

## The in-app guide: How to use the CMS

This guide is not a document. It is a set of CMS pages under `/help/how-to-use-the-cms`,
and the application creates and updates them itself at start-up, in every environment.
A release that changes the CMS carries the pages that explain the change.

| Part | Where |
|---|---|
| The text | `cms/pages/*.md`, one file for each page |
| The screenshots | `src/DfE.CheckPerformanceData.Web/wwwroot/assets/cms-help/*.png` |
| What the application imports | `src/DfE.CheckPerformanceData.Web/Data/Import/cms-guide.json` |
| The tools | `cms/tools` |

The Markdown and the screenshots are the source of truth. The JSON file is generated from
them: do not edit it by hand.

### Changing the guide

```powershell
docker compose up -d --build              # from the repo root; wait for http://localhost:8080/healthcheck
cd docs/user-guides/cms/tools
npm install
npm run capture                           # all screenshots, or: npm run capture -- versions-tab tree-menu
npm run build                             # writes Data/Import/cms-guide.json
npm run preview                           # imports it into the local stack so you can read it
```

1. Edit the Markdown. The comment at the top of `build-bundle.mjs` describes the front
   matter and the few things that go beyond plain Markdown.
2. If a screen has changed, recapture it. To add a screenshot, add an entry to `shots` in
   `capture.mjs`.
3. Run `npm run build`, then `npm run preview`, and read the result at
   `/help/how-to-use-the-cms`.
4. Commit the Markdown, the screenshots and the JSON file together.

`npm run build` fails if a page links to a page that does not exist, shows a screenshot
that has not been captured, has an image without alt text, or if a screenshot is not
shown on any page. `ShippedContentImportTests` checks the same things in the build.

`capture.mjs` and `preview.mjs` sign in through `/dev/impersonate`, so they only work
against the compose stack. Set `CPD_BASE_URL` if yours is not on port 8080. Before it
takes the first screenshot, `capture.mjs` imports an example page, *Autumn checking
exercise*, under Guidance, and seeds a week of sample search data if there is none. Both
stay in your local database afterwards.

### How it reaches each environment

`cms-guide.json` is listed in `src/DfE.CheckPerformanceData.Web/Data/Import/manifest.json`,
and the application imports the files in that manifest at start-up. See
[Content shipped with the service](../guidance-and-content-staging.md#content-shipped-with-the-service).

The guide is listed for every environment, with `"existing": "replaceOlder"`. For each
page of the guide:

- if the page does not exist, it is created
- if it was last changed before this guide was built, it is replaced
- if it has been changed since, it is left alone, so a correction made in one environment
  is kept until a newer guide is released
- if it has been deleted, it stays deleted, and can be restored from *Deleted pages*

"When this guide was built" is the `exportedAtUtc` date in the JSON file. `npm run build`
moves it forward only when the content has changed.

The file name of a Markdown page is the last part of its address, and its identity in the
database is worked out from that name. Renaming a file therefore creates a new page and
leaves the old one behind in every environment that already has it. To retire or rename
a page, delete the old page in each environment as well.
