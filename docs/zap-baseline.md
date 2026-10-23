# OWASP ZAP baseline scan

Every pull request with a review app (the `deploy` label) gets a "ZAP baseline scan" check (#618,
part of the OWASP Top 10 (2025) hardening #607). The job is in
`.github/workflows/build-and-deploy.yml` and runs beside the smoke tests, after "Deployment To
Review". The scan is the automation plan `.github/zap/plan.yaml`.

## What it does

[OWASP ZAP](https://www.zaproxy.org/) crawls the review app for up to three minutes and runs its
passive rules over every response: headers, cookies, what is disclosed in the markup. It attacks
nothing. The check looks at what a browser is actually sent, after the AKS ingress and Front Door,
where the unit tests look at what the code sets.

## What it does not do

- **It does not sign in.** Only the anonymous pages are scanned: home, guidance and content pages,
  search, cookies, privacy, accessibility, contact, `robots.txt`. Everything under `/dev/` is out
  of scope, so the dev impersonation links in a review app's header are never followed. The
  signed-in pages need a DfE Sign-in session or the dev impersonation cookie, and an unattended
  crawler following every link there (sign out, withdraw, delete) would change the review app's
  data. An authenticated scan is a follow-up under #607.
- **It submits no forms** (`postForm: false` on the spider job). A review app creates real Zendesk
  tickets and the contact form is anonymous. GET forms, such as search, are still followed.
- It does not run the active scan, and it does not scan the deployed environments. A manual scan
  of development, QA, preproduction or production is a follow-up under #607.

## Reading a result

The job summary on the run page carries the counts and the report: each rule that fired, how many
URLs, and the URLs. The same report in HTML, Markdown and JSON, with the summary counts, is the
run's `zap-baseline` artefact (kept 90 days). A rule's documentation is at
`https://www.zaproxy.org/docs/alerts/<rule id>/`.

The check is green when every rule that fired is accepted in the plan's rules. It is red when
something new fired.

## Triaging a new finding

In this order:

1. **Fix it in code** when the fix is small and clearly right: a header missing on one response
   type, a cookie flag, a disclosed timestamp. Add the unit test first
   (`SecurityHeadersMiddlewareTests` for headers).
2. **Accept it** with an `IGNORE` entry under the plan's `outputSummary` job when an existing
   control covers the risk, or the rule is informational. The `customMessage` names the control,
   or says "informational". The reviewer judges the reason; an IGNORE without one is not accepted.
3. **Leave it red** (`WARN`) when it needs more than the pull request. Raise a ticket under #607
   and put its number in the message. If the team would rather the check were green meanwhile,
   use IGNORE with the ticket number instead.

Only rules a report has raised are listed, so a rule that a new ZAP release adds is noticed as a
red check and triaged the same way. The ZAP image is `ghcr.io/zaproxy/zaproxy:stable` by tag,
from a `run:` step: neither Dependabot nor the pin check (`docs/dependabot.md#pins`) covers it, a
digest there would be nobody's job to keep current, and a stale scanner is worse than a moving
one. A new release surfaces as a new finding to triage, never as a silent change to an accepted
one.

To collect a fresh baseline, set the job's `ZAP_GATE` to `"false"`, push, read the report, and
set it back in the same pull request.

## Why our own plan

`zaproxy/action-baseline` runs `zap-baseline.py`, which since ZAP 2.17 generates an automation
plan whose spider job carries `postForm: true` as a default that overrides any `-config` option.
A rehearsal with it submitted the contact form and, having followed the dev impersonation links,
admin forms. `.github/zap/plan.yaml` is that generated plan with `postForm: false` and the `/dev/`
exclusion.

## What is accepted and why

| Rule | Decision | Reason |
|---|---|---|
| 2 | IGNORE | Private IP Disclosure: false positive, 10.02.68.86 is SVG path coordinates in assets/images/favicon.svg, not an address \| |
| 10015 | IGNORE | Re-examine Cache-control Directives: informational on anonymous pages (no-cache, no-store without private/must-revalidate; robots.txt sends none); the personal-data responses carry no-store (#615) \| |
| 10027 | IGNORE | Suspicious Comments: the matched block is the session diagnostic footer, which DiagnosticFooterMiddleware emits only outside Production (env.IsProduction() short-circuit, covered by DiagnosticFooterMiddlewareTests); the other hits are explanatory JS comments in _GtmHead and _ClarityHead. Follow-up #677: switch the footer off on review apps \| |
| 10054 | IGNORE | Cookie with SameSite Attribute None: .AspNetCore.Correlation.* is the OpenID Connect handler's correlation cookie for the DfE Sign-in redirect; ASP.NET Core sets it SameSite=None by default so the cross-site return carries it (the Secure-cookie rule 10011 passed) \| |
| 10055 | IGNORE | CSP: style-src 'unsafe-inline' and the Google wildcard hosts are needed by GOV.UK Frontend and Google Tag Manager; script-src is nonce-only (#611) \| |
| 10096 | IGNORE | Timestamp Disclosure: informational, 1904761905 is a numeric constant in minified GOV.UK Frontend CSS, not a timestamp \| |
| 10109 | IGNORE | Modern Web Application: informational; the matched markup is a GOV.UK placeholder link and the service is server-rendered \| |
| 10112 | IGNORE | Session Management Response Identified: informational; the antiforgery cookie \| |

## Running it locally

Start the compose stack (`docker compose up -d db azurite web`), copy the plan into a writable
work folder with an empty `zap-out` folder beside it, then:

```bash
docker run --rm -e ZAP_TARGET=http://host.docker.internal:8080 \
  -v "<work folder>:/zap/wrk:rw" ghcr.io/zaproxy/zaproxy:stable \
  zap.sh -cmd -autorun /zap/wrk/plan.yaml
```

The reports and `summary.json` land in `<work folder>/zap-out`. Under Git Bash on Windows, prefix
the command with `MSYS_NO_PATHCONV=1`, or the shell turns `/zap/wrk/plan.yaml` into a Windows path.
The local web runs as Development, so HSTS and the cookie `Secure` flag would be reported there
and not on a review app.
