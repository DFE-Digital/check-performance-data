# Admin dashboard — engagement & amendment metrics (PBI 288143)

`/admin/dashboard` shows CYPMD admins nine aggregated figures for a selected **open**
checking window. Access is gated by the `dashboard` admin section grant
(`RequireAdminSection`), so users without the grant receive a 404 — the codebase's standard
behaviour for admin surfaces. The PBI's "Access Denied" acceptance criterion is met in
substance (no metric data is reachable) but deliberately not rendered as a visible
"Access Denied" page, to keep the admin surface hidden from URL discovery.

## Metric definitions

| Metric | Definition |
|---|---|
| Eligible schools | Schools with a pupil file (`data/{laestab}_pupils.json`) in the window's blob container |
| Logged in | Distinct eligible schools with an `OrganisationLogin` row timestamped inside the window's start–end dates |
| Not logged in | Eligible schools minus Logged in |
| Submitted amendments | Distinct eligible schools (by laestab) whose URN has a submitted request; the URN→laestab mapping comes from the window's own login rows |
| Logged in (not submitted) | Logged in minus Submitted amendments |
| Total individual pupil amendment requests | Requests with status SubmittedUnCommitted or SubmittedCommitted (drafts, withdrawn and not-submitted excluded) |
| Auto-approved / Auto-rejected / Requests requiring scrutiny | Submitted requests by rules-engine `Outcome`; undecided requests count only in the total |

All five engagement tiles count the same population (eligible schools) by the same key
(laestab). One consequence: a submitter whose login row failed to record (recording is
deliberately non-blocking) drops off "Submitted amendments", though its requests still
count in the request tiles.

## Login tracking

Nothing recorded sign-ins before this feature. The DfE Sign-In `OnTokenValidated` hook now
appends an `OrganisationLogins` row (URN, digits-only laestab, organisation name,
UTC timestamp) after successful claims enrichment. Recording is wrapped in try/catch — a
failure can never block sign-in. Rows are append-only; the dashboard deduplicates at query
time. Logins recorded before the feature shipped obviously do not exist, so "Logged in"
undercounts for windows that opened before deployment.

Window boundaries are compared as UTC: window start/end dates are stored without a
timezone (window admins enter wall-clock times) and the login query treats them as UTC,
matching how the deployed service — whose pods run UTC — evaluates window opening
elsewhere. During British Summer Time the boundary is therefore one hour later than UK
wall-clock. A service-wide decision on window timezone handling is out of this feature's
scope.

## Refresh behaviour

Figures are computed on demand and cached per window for `Dashboard:RefreshMinutes`
(default 15, floored at 1 — a configured 0 or negative would otherwise be rejected by
`IMemoryCache` and fail the page). The page shows the computation time and reloads itself
via a small progressive-enhancement script once the cache is due to expire; without
JavaScript the figures simply refresh on the next manual reload after expiry. The cache is
per pod, so with two replicas the two pods may refresh at slightly different moments — two
admins (or one admin whose auto-refresh lands on the other pod) can briefly see different
figures and "Last refreshed" times. Accepted for a metrics view.

Because the reload is auto-updating content, a "Stop automatic refresh" button cancels it
(WCAG 2.2 SC 2.2.2). The button only appears once the script has scheduled a reload, so it
is absent — along with the auto-refresh itself — when JavaScript is unavailable.

## Data protection note

`OrganisationLogins` stores organisation-level data only: URN, digits-only laestab,
organisation name and a UTC timestamp. The DfE Sign-In user id was captured in an early
revision but dropped before release on data-minimisation grounds — no feature read it and
the table has no retention limit. Login inserts are also excluded from the audit trail
(`audit_entries`), which would otherwise have kept a second copy of every row. Retention
of the remaining append-only rows is still an open question for the service's DPIA.
