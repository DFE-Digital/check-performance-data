# Check Performance Data

## Vision

Enable establishments to check their performance data and submit amendment requests during the relevant checking windows. Give authorised staff the tools to administer the service and investigate support issues.

Project baseline confirmed by the user (D-6).

## Who it is for

- Establishment users checking data and managing amendment requests.
- Authorised administrators managing checking windows, requests, content and service operations.

## Principles

- Restrict access to establishment data and administrative functions according to the user's permissions.
- Preserve existing establishment journeys when adding administrative features.
- Make changes attributable to the person performing them.
- Keep sensitive configuration out of tracked files and output; follow the repository's secret-handling instructions.
- Treat development authentication helpers as development facilities, not authority for production access.

## Platform

- Existing C# / ASP.NET Core application on .NET 10; retain the existing project structure (D-1, confirmed by D-6).
- Existing deployment infrastructure uses Terraform and AKS; this planning work proposes no hosting change (D-1, confirmed by D-6).

## Out of scope

- Replacing the existing authentication provider or deployment platform as part of administrative feature planning.
- Treating administrator access as unrestricted access to all establishment actions.

## Increments

No numbered increments have been created yet. The next candidate is **feature: Admin establishment impersonation**, following completion of this project baseline.

| Folder | Type | Title | Status |
|---|---|---|---|
