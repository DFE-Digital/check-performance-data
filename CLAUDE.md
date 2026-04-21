# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
# Build
dotnet build src/DfE.CheckPerformanceData.slnx

# Run all tests
dotnet test src/DfE.CheckPerformanceData.slnx

# Run a single test project
dotnet test tests/DfE.CheckPerformanceData.UnitTests/

# Run a specific test
dotnet test tests/DfE.CheckPerformanceData.UnitTests/ --filter "FullyQualifiedName~MyTestName"

# Start local dependencies (PostgreSQL + Azurite)
docker compose up --build -d

# Run the web app
dotnet run --project src/DfE.CheckPerformanceData.Web
```

## Architecture

The solution follows a clean/onion architecture with five projects:

- **Domain** — pure domain models, no external dependencies
- **Application** — business logic, service interfaces and implementations, DTOs
- **Persistence** — EF Core `PortalDbContext`, repositories, entity configurations, dev data seeding
- **Infrastructure** — authentication (DfE SignIn OIDC or fake dev auth), external HTTP clients
- **Web** — ASP.NET Core controllers and Razor views using GOV.UK Frontend components
- **RulesEngineWorker** — background worker that polls Azure Storage Queues

Each layer registers its own dependencies via `DependencyManager.cs` extension methods called from `Program.cs`.

## Key Patterns

**Authentication:** Controlled by config. Local dev uses `AddFakeSignInAuthentication()` (hardcoded claims). Production uses `AddDfeSignInAuthentication()` (OpenID Connect against DfE SignIn).

**Database:** PostgreSQL via EF Core with Npgsql. Fluent API configurations live in `Persistence/Configurations/`. Dev data is seeded via `DevDataSeeder`. Local dev uses docker-compose for the database.

**DTO Mapping:** Uses Mapperly (source-generation) for mapping between domain entities and DTOs — avoid writing manual mapping code.

**Versioning:** `WikiPage` and `ContentBlock` entities maintain version history. Changes create new version records rather than updating in place.

**Queue Processing:** The `RulesEngineWorker` communicates via Azure Storage Queues (emulated locally with Azurite from the docker-compose stack).

**Frontend:** GOV.UK Frontend ASP.NET Core (`GovUk.Frontend.AspNetCore`) for all UI components. Markdown content is rendered via Markdig and sanitised with HtmlSanitizer.

**Testing:** xUnit + NSubstitute. Unit tests mock at the service boundary — repositories and external services are substituted.

## Package Management

Centralised package versions are declared in `src/Directory.Packages.props`. Add new packages there rather than directly in `.csproj` files.
