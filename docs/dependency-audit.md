# Dependency Audit: Skipped NuGet Package Updates

**Date**: 2026-09-07  
**Branch**: `392-update-nuget-packages`  
**Reason**: Every package that could not be updated in the sweep is listed here with the reason, so a future issue can address it deliberately.

---

## Breaking / Major Version Skips (6 packages)

| Package | Current | Target | Reason |
|---------|---------|--------|--------|
| `Refit` | 13.1.0 | 15.2.0 | Major version jump (13→15); likely breaking API changes |
| `Refit.HttpClientFactory` | 13.1.0 | 15.2.0 | Major version jump (13→15); likely breaking API changes |
| `Refit.Newtonsoft.Json` | 13.1.0 | 15.2.0 | Major version jump (13→15); likely breaking API changes |
| `NSubstitute` | 5.3.0 | 6.2.0 | Major version jump (5→6); breaking API changes |
| `SixLabors.ImageSharp` | 3.1.12 | 4.1.1 | Major version jump (3→4); breaking API changes |
| `xunit.runner.visualstudio` | 3.1.5 | 4.0.0 | Major version jump (3→4); breaking API changes |

---

## SDK-Aligned Framework Skips (13 packages)

These packages are version-locked to the .NET SDK and serviced via SDK/runtime updates, not a dependency sweep.

| Package | Version | Reason |
|---------|---------|--------|
| `Microsoft.EntityFrameworkCore` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.EntityFrameworkCore.Tools` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.Extensions.Caching.Abstractions` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.Extensions.Configuration.Abstractions` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.Extensions.DependencyInjection` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.Extensions.Http` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.AspNetCore.DataProtection` | 10.0.9 | SDK-aligned; serviced via runtime updates |
| `Microsoft.AspNetCore.TestHost` | 10.0.7 | SDK-aligned; serviced via runtime updates |
| `System.Security.Cryptography.Xml` | 10.0.10 | SDK-aligned; serviced via runtime updates |

---

## Transitive Advisory

| Package | Version | Advisory | Reason |
|---------|---------|----------|--------|
| `SSH.NET` | 2024.2.0 | NU1903 / GHSA-q939-rpr3-3284 | High-severity transitive; no fix available in current dependency graph |

---

## Packages Updated in This Sweep

For completeness, the following 24 packages were updated (13 source via CPM, 8 test-deps reconciled, 3 provisional minors):

### Source Dependencies (CPM)

| Package | From | To |
|---------|------|----|
| `Azure.Extensions.AspNetCore.DataProtection.Blobs` | 1.5.3 | 1.5.4 |
| `Azure.Storage.Blobs` | 12.29.1 | 12.29.2 |
| `Microsoft.AspNetCore.Authentication` | 2.3.11 | 2.3.12 |
| `Microsoft.AspNetCore.Authentication.Cookies` | 2.3.11 | 2.3.12 |
| `Microsoft.AspNetCore.Authentication.Core` | 2.3.11 | 2.3.12 |
| `Microsoft.AspNetCore.Http` | 2.3.11 | 2.3.12 |
| `Microsoft.AspNetCore.Http.Extensions` | 2.3.11 | 2.3.12 |
| `Microsoft.Identity.Web` | 4.13.0 | 4.14.2 |
| `System.IdentityModel.Tokens.Jwt` | 8.19.1 | 8.22.0 |
| `HtmlSanitizer` | 9.1.982 | 9.2.1039 |
| `PdfPig` | 0.1.15 | 0.1.16 |
| `Serilog.Sinks.PostgreSQL.Alternative` | 4.2.0 | 4.3.0 |
| `GovUk.Frontend.AspNetCore` | 4.2.1 | 4.4.0 |

### Provisional Minors (applied; revert if breaking)

| Package | From | To |
|---------|------|----|
| `AngleSharp` | 1.7.1 | 1.8.0 |
| `DfeAnalytics.AspNetCore` | 0.5.2 | 0.6.2 |
| `DfeAnalytics.Core` | 0.5.2 | 0.6.2 |

### Test Dependencies (reconciled across .csproj files + CPM)

| Package | From | To |
|---------|------|----|
| `coverlet.collector` | 6.0.4 / 10.0.0 | 10.0.1 |
| `Microsoft.NET.Test.Sdk` | 17.14.0 / 18.4.0 | 18.9.0 |
| `Microsoft.Playwright` | 1.59.0 | 1.62.0 |
| `Microsoft.Playwright.Xunit` | 1.59.0 | 1.62.0 |
| `Testcontainers.Azurite` | 4.7.0 | 4.15.0 |
| `Testcontainers.PostgreSql` | 4.7.0 | 4.15.0 |
| `Xunit.SkippableFact` | 1.5.23 | 1.5.85 |
| `Moq` | 4.18.2 | 4.20.72 |
