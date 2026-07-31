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
| Submitted amendments | Distinct organisation URNs with a submitted request for the window |
| Logged in (not submitted) | Logged-in schools whose URN has no submitted request |
| Total individual pupil amendment requests | Requests with status SubmittedUnCommitted or SubmittedCommitted (drafts, withdrawn and not-submitted excluded) |
| Auto-approved / Auto-rejected / Requests requiring scrutiny | Submitted requests by rules-engine `Outcome`; undecided requests count only in the total |

## Login tracking

Nothing recorded sign-ins before this feature. The DfE Sign-In `OnTokenValidated` hook now
appends an `organisation_logins` row (user id, URN, digits-only laestab, organisation name,
UTC timestamp) after successful claims enrichment. Recording is wrapped in try/catch — a
failure can never block sign-in. Rows are append-only; the dashboard deduplicates at query
time. Logins recorded before the feature shipped obviously do not exist, so "Logged in"
undercounts for windows that opened before deployment.

## Refresh behaviour

Figures are computed on demand and cached per window for `Dashboard:RefreshMinutes`
(default 15). The page shows the computation time and reloads itself via a small
progressive-enhancement script once the cache is due to expire; without JavaScript the
figures simply refresh on the next manual reload after expiry. The cache is per pod, so
with two replicas the two pods may refresh at slightly different moments.
