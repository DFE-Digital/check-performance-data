# Check performance data

## Vision
Support schools checking performance data, with development tooling that lets contributors safely inspect, change and verify the service.

## Who it is for
School users and service contributors.

## Principles
Keep secrets out of tracked files. Preserve application behaviour when adding development tooling. Enforce coding-agent filesystem access through container mounts, independent of approvals.

## Platform
Existing ASP.NET Core application; rootless Podman Compose for the repository-local coding-agent environment (D-1).

## Out of scope
Granting agents access to unrelated host files or production credentials.

## Increments
| Folder | Type | Title | Status |
|---|---|---|---|
| 001-feature-isolated-agent-environment | feature | Isolated agent environment | built |
