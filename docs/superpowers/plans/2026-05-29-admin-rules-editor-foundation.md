# Admin Rules Editor — Milestone 1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the non-UI foundation for the admin rules editor — a versioned, validated, audited read/write path for `rules.json` and `country-languages.json` — so later milestones only have to add Razor screens on top.

**Architecture:** Mirror the existing port/adapter pattern (`ContentBlockService` + `IContentBlockRepository`). Application defines ports (`IRulesConfigStore` for the blob, `IRulesConfigVersionRepository` for the DB) and an orchestrating `RulesConfigService`; Persistence implements the repository over `IPortalDbContext`; Infrastructure implements the blob store over `BlobServiceClient`. Every save validates first, checks the blob ETag (optimistic concurrency), writes the blob, appends an append-only version snapshot, and writes an `AuditEntry`.

**Tech Stack:** .NET 10 / C# 12, EF Core 10 (Npgsql/PostgreSQL), Azure.Storage.Blobs. **Tests use xUnit** (`[Fact]`); integration tests use **Testcontainers** (PostgreSQL today; this plan adds an Azurite container). Spec: `docs/superpowers/specs/2026-05-29-admin-rules-editor-design.md`.

> ⚠️ The repo's `CLAUDE.md` lists NUnit, but the actual test projects use **xUnit**. Follow the existing files: `[Fact]`, constructor injection, `Assert.Equal/True/False/Contains/Empty`, `[Collection(nameof(PostgresCollection))]`. Do not introduce NUnit.

---

## Milestone roadmap (context — only M1 is detailed below)

- **M1 — Foundation (this plan):** config types, version entity + migration, blob store port + adapter, version repository port + adapter, validator duplicate-key rule, lookups validator, `RulesConfigService` orchestration, DI wiring. No UI.
- **M2 — Read-only admin surface:** `RulesConfigNavEntry`, `AdminRulesController`, landing + outcome-list + branch-list + history views (read-only), all admin-gated.
- **M3 — Editing:** recursive predicate partial + flat form-binding model, branch editor with select-then-group, save wired to `RulesConfigService`, lookups editor.
- **M4 — Outcome add/remove + deletion guard + rollback UI:** add outcome (seeded `otherwise → Scrutiny`), remove with the `WhatToChangeToOutcomeKey` hard block + connected-`ChangeRequest` display + typed confirm, version-history rollback action.

Each milestone is its own plan file. Build them in order.

---

## File structure (Milestone 1)

**Create**
- `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigType.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigBlob.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigConflictException.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/IRulesConfigStore.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigVersionDto.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/IRulesConfigVersionRepository.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/LookupsValidator.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigSaveResult.cs`
- `src/DfE.CheckPerformanceData.Application/RulesConfig/IRulesConfigService.cs` + `RulesConfigService.cs`
- `src/DfE.CheckPerformanceData.Persistence/Entities/RulesConfigVersion.cs`
- `src/DfE.CheckPerformanceData.Persistence/Configurations/RulesConfigVersionConfiguration.cs`
- `src/DfE.CheckPerformanceData.Persistence/Repositories/RulesConfigVersionRepository.cs`
- `src/DfE.CheckPerformanceData.Infrastructure/RulesEngine/BlobRulesConfigStore.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RuleSetValidatorDuplicateKeyTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/LookupsValidatorTests.cs`
- `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RulesConfigServiceTests.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/RulesConfigVersionRepositoryTests.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/Fixtures/AzuriteFixture.cs`
- `tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/BlobRulesConfigStoreTests.cs`

**Modify**
- `src/DfE.CheckPerformanceData.Application/RulesEngine/RuleSetValidator.cs` — add duplicate-outcome-key check.
- `src/DfE.CheckPerformanceData.Persistence/Contexts/IPortalDbContext.cs` + `PortalDbContext.cs` — add `DbSet<RulesConfigVersion>`.
- The Persistence DI registration (where `IContentBlockRepository` is registered) — register the new repository.
- `src/DfE.CheckPerformanceData.Application/DependencyManager.cs` — register `LookupsValidator` + `IRulesConfigService`.
- `src/DfE.CheckPerformanceData.Web/Program.cs` — register `IRulesConfigStore` + bind `BlobRulesProviderOptions`.
- A new EF migration under `src/DfE.CheckPerformanceData.Persistence/Migrations/`.
- `tests/DfE.CheckPerformanceData.IntegrationTests/DfE.CheckPerformanceData.IntegrationTests.csproj` — add `Testcontainers.Azurite`.

> All commands assume working directory = repo root `C:\Repos\DfE\check-performance-data`. No `.sln`; the EF startup project is the Web project.

---

## Task 1: Duplicate-outcome-key validation in `RuleSetValidator`

The validator enforces unique branch ids within an outcome but not unique outcome *keys*. Outcome add/remove (M4) needs this.

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Application/RulesEngine/RuleSetValidator.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RuleSetValidatorDuplicateKeyTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RuleSetValidatorDuplicateKeyTests.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.RulesConfig;

public class RuleSetValidatorDuplicateKeyTests
{
    private static RuleBranch Otherwise(string id) =>
        new(id, DecisionStatus.Scrutiny, Predicate.Otherwise.Instance);

    private static OutcomeRules Outcome(string key) =>
        new(key, key, new[] { Otherwise($"{key}-DEF") });

    [Fact]
    public void Validate_duplicate_outcome_keys_reports_error()
    {
        var rules = new RuleSet("v1", DateTimeOffset.UnixEpoch,
            new[] { Outcome("Inclusion"), Outcome("Inclusion") });

        var result = new RuleSetValidator().Validate(rules);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("duplicate outcome key 'Inclusion'"));
    }

    [Fact]
    public void Validate_unique_outcome_keys_is_valid()
    {
        var rules = new RuleSet("v1", DateTimeOffset.UnixEpoch,
            new[] { Outcome("Inclusion"), Outcome("Deceased") });

        var result = new RuleSetValidator().Validate(rules);

        Assert.True(result.IsValid);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~RuleSetValidatorDuplicateKeyTests`
Expected: FAIL — `Validate_duplicate_outcome_keys_reports_error` fails (no duplicate-key error produced).

- [ ] **Step 3: Implement the check**

In `RuleSetValidator.Validate`, immediately before the final `return errors.Count == 0 ? ... : ...;`, add:

```csharp
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in rules.Outcomes)
        {
            if (!string.IsNullOrWhiteSpace(outcome.Key) && !seenKeys.Add(outcome.Key))
            {
                errors.Add($"RuleSet has duplicate outcome key '{outcome.Key}'.");
            }
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~RuleSetValidatorDuplicateKeyTests`
Expected: PASS (both).

- [ ] **Step 5: Confirm no regression in the existing validator suite**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~RuleSetValidator`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Application/RulesEngine/RuleSetValidator.cs tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RuleSetValidatorDuplicateKeyTests.cs
git commit -m "feat(rules): reject duplicate outcome keys in RuleSetValidator"
```

---

## Task 2: `RulesConfigType` + `LookupsValidator`

**Files:**
- Create: `src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigType.cs`
- Create: `src/DfE.CheckPerformanceData.Application/RulesConfig/LookupsValidator.cs`
- Test: `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/LookupsValidatorTests.cs`

- [ ] **Step 1: Create the enum**

`src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigType.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>Which config document a version snapshot / save refers to.</summary>
public enum RulesConfigType
{
    Rules,
    Lookups
}
```

- [ ] **Step 2: Write the failing test**

`tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/LookupsValidatorTests.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.RulesConfig;

public class LookupsValidatorTests
{
    private static Lookups Map(params (string code, string[] langs)[] rows) =>
        new(rows.ToDictionary(r => r.code, r => (IReadOnlyList<string>)r.langs));

    [Fact]
    public void Valid_map_passes()
    {
        var result = new LookupsValidator().Validate(Map(("GB", new[] { "English", "Welsh" })));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Empty_country_code_fails()
    {
        var result = new LookupsValidator().Validate(Map(("", new[] { "English" })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty country code"));
    }

    [Fact]
    public void Country_with_no_languages_fails()
    {
        var result = new LookupsValidator().Validate(Map(("GB", Array.Empty<string>())));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has no languages"));
    }

    [Fact]
    public void Blank_language_fails()
    {
        var result = new LookupsValidator().Validate(Map(("GB", new[] { "English", "  " })));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("'GB' has a blank language"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~LookupsValidatorTests`
Expected: FAIL — compile error, `LookupsValidator` does not exist.

- [ ] **Step 4: Implement `LookupsValidator`**

`src/DfE.CheckPerformanceData.Application/RulesConfig/LookupsValidator.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed record LookupsValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static LookupsValidationResult Success() => new(true, Array.Empty<string>());
    public static LookupsValidationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}

/// <summary>
/// Validates a <see cref="Lookups"/> map before it replaces the live country-languages
/// blob. Errors map directly onto a GOV.UK error summary.
/// </summary>
public sealed class LookupsValidator
{
    public LookupsValidationResult Validate(Lookups lookups)
    {
        ArgumentNullException.ThrowIfNull(lookups);

        var errors = new List<string>();
        foreach (var (code, languages) in lookups.CountryLanguages)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add("Lookups has an empty country code.");
                continue;
            }
            if (languages.Count == 0)
            {
                errors.Add($"Country '{code}' has no languages.");
            }
            if (languages.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Country '{code}' has a blank language.");
            }
        }

        return errors.Count == 0 ? LookupsValidationResult.Success() : LookupsValidationResult.Failure(errors);
    }
}
```

(The dictionary key is unique, so duplicate country codes can't occur here; the UI prevents adding an existing code.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~LookupsValidatorTests`
Expected: PASS (all four).

- [ ] **Step 6: Commit**

```bash
git add src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigType.cs src/DfE.CheckPerformanceData.Application/RulesConfig/LookupsValidator.cs tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/LookupsValidatorTests.cs
git commit -m "feat(rules): add RulesConfigType and LookupsValidator"
```

---

## Task 3: Application ports and DTOs (build only — no behaviour)

**Files:**
- Create: `RulesConfigBlob.cs`, `RulesConfigConflictException.cs`, `IRulesConfigStore.cs`, `RulesConfigVersionDto.cs`, `IRulesConfigVersionRepository.cs` (all under `src/DfE.CheckPerformanceData.Application/RulesConfig/`)

- [ ] **Step 1: Blob read-result + conflict exception**

`RulesConfigBlob.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>Raw content of a config blob plus its current ETag (for optimistic concurrency).</summary>
public sealed record RulesConfigBlob(string Content, string? ETag);
```

`RulesConfigConflictException.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Thrown by <see cref="IRulesConfigStore"/> when a write's expected ETag no longer matches
/// the blob — i.e. someone else saved since this edit session loaded.
/// </summary>
public sealed class RulesConfigConflictException : Exception
{
    public RulesConfigConflictException(string message) : base(message) { }
}
```

- [ ] **Step 2: Blob store port**

`IRulesConfigStore.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Read/write access to the two rules-config blobs (rules.json, country-languages.json).
/// Reads return content + ETag; writes pass the expected ETag so a concurrent change is
/// rejected with <see cref="RulesConfigConflictException"/> rather than silently clobbered.
/// Pass <c>expectedETag = null</c> only to create a blob that does not yet exist.
/// </summary>
public interface IRulesConfigStore
{
    Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default);
    Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default);
}
```

- [ ] **Step 3: Version DTO + repository port**

`RulesConfigVersionDto.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed record RulesConfigVersionDto
{
    public int Id { get; init; }
    public RulesConfigType ConfigType { get; init; }
    public int VersionNumber { get; init; }
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
```

`IRulesConfigVersionRepository.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Append-only history of saved config documents, plus the audit write. Implemented in the
/// Persistence layer over IPortalDbContext.
/// </summary>
public interface IRulesConfigVersionRepository
{
    Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default);
    Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
        DateTime createdAt, CancellationToken ct = default);
    Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default);
    Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
        DateTime timestamp, CancellationToken ct = default);
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/DfE.CheckPerformanceData.Application`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Application/RulesConfig/
git commit -m "feat(rules): add IRulesConfigStore + version repository ports and DTOs"
```

---

## Task 4: `RulesConfigVersion` entity, EF config, DbContext, migration

**Files:**
- Create: `src/DfE.CheckPerformanceData.Persistence/Entities/RulesConfigVersion.cs`
- Create: `src/DfE.CheckPerformanceData.Persistence/Configurations/RulesConfigVersionConfiguration.cs`
- Modify: `IPortalDbContext.cs`, `PortalDbContext.cs`
- Create: migration under `src/DfE.CheckPerformanceData.Persistence/Migrations/`

- [ ] **Step 1: Entity**

`src/DfE.CheckPerformanceData.Persistence/Entities/RulesConfigVersion.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// Append-only snapshot of a saved rules-config document. Mirrors ContentBlockVersion but is
/// standalone (no parent entity) and discriminated by ConfigType.
/// </summary>
public sealed class RulesConfigVersion
{
    public int Id { get; set; }
    public RulesConfigType ConfigType { get; set; }
    public int VersionNumber { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
```

> Persistence already references Application (it implements Application ports elsewhere). If `dotnet build` reports `RulesConfigType` not found, add a `ProjectReference` from the Persistence csproj to the Application csproj — but verify first; it should already be present.

- [ ] **Step 2: EF configuration**

`src/DfE.CheckPerformanceData.Persistence/Configurations/RulesConfigVersionConfiguration.cs`:

```csharp
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class RulesConfigVersionConfiguration : IEntityTypeConfiguration<RulesConfigVersion>
{
    public void Configure(EntityTypeBuilder<RulesConfigVersion> builder)
    {
        builder.Property(v => v.ConfigType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Content).IsRequired();
        builder.HasIndex(v => new { v.ConfigType, v.VersionNumber }).IsUnique();
    }
}
```

- [ ] **Step 3: DbSet on the interface**

In `src/DfE.CheckPerformanceData.Persistence/Contexts/IPortalDbContext.cs`, add alongside the other `DbSet` members:

```csharp
    DbSet<RulesConfigVersion> RulesConfigVersions { get; }
```

- [ ] **Step 4: DbSet on the context**

In `src/DfE.CheckPerformanceData.Persistence/Contexts/PortalDbContext.cs`, mirror how `ContentBlockVersions` is declared (read that line first and match its exact style — likely):

```csharp
    public DbSet<RulesConfigVersion> RulesConfigVersions => Set<RulesConfigVersion>();
```

Confirm `OnModelCreating` applies configurations from the assembly (`ApplyConfigurationsFromAssembly(...)`). If configurations are instead registered one-by-one, add `modelBuilder.ApplyConfiguration(new RulesConfigVersionConfiguration());` next to the `ContentBlockVersionConfiguration` line.

- [ ] **Step 5: Build before generating the migration**

Run: `dotnet build src/DfE.CheckPerformanceData.Persistence`
Expected: Build succeeded.

- [ ] **Step 6: Generate the migration**

Run:
```bash
dotnet ef migrations add AddRulesConfigVersion --project src/DfE.CheckPerformanceData.Persistence --startup-project src/DfE.CheckPerformanceData.Web
```
Expected: a `*_AddRulesConfigVersion.cs` (+ `.Designer.cs`) under `Persistence/Migrations/`. Open `Up()` and confirm it creates a `RulesConfigVersions` table with a unique `(ConfigType, VersionNumber)` index and a `text` `Content` column.

> If `dotnet ef` is missing: `dotnet tool restore` (if pinned as a local tool) or `dotnet tool install --global dotnet-ef`.

- [ ] **Step 7: Build the Web host (applies migrations at startup via MigrateDatabaseAsync)**

Run: `dotnet build src/DfE.CheckPerformanceData.Web`
Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add src/DfE.CheckPerformanceData.Persistence/Entities/RulesConfigVersion.cs src/DfE.CheckPerformanceData.Persistence/Configurations/RulesConfigVersionConfiguration.cs src/DfE.CheckPerformanceData.Persistence/Contexts/ src/DfE.CheckPerformanceData.Persistence/Migrations/
git commit -m "feat(rules): add RulesConfigVersion entity and migration"
```

---

## Task 5: `RulesConfigVersionRepository` (Persistence adapter)

Uses the existing `PostgresFixture` (Testcontainers PostgreSQL) and the `PostgresCollection` xUnit collection.

**Files:**
- Create: `src/DfE.CheckPerformanceData.Persistence/Repositories/RulesConfigVersionRepository.cs`
- Test: `tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/RulesConfigVersionRepositoryTests.cs`

- [ ] **Step 1: Write the failing integration test**

`tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/RulesConfigVersionRepositoryTests.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;
using Xunit;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesConfig;

[Collection(nameof(PostgresCollection))]
public sealed class RulesConfigVersionRepositoryTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private RulesConfigVersionRepository NewRepo() => new(_fixture.CreateContext());

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""RulesConfigVersions"" RESTART IDENTITY; TRUNCATE ""AuditEntries"" RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Add_then_list_returns_newest_first()
    {
        await TruncateAsync();

        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 1, "{\"v\":1}", "alice", DateTime.UtcNow.AddMinutes(-2));
        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 2, "{\"v\":2}", "bob", DateTime.UtcNow);

        var list = await NewRepo().ListAsync(RulesConfigType.Rules);

        Assert.Equal(2, list.Count);
        Assert.Equal(2, list[0].VersionNumber);     // newest first
        Assert.Equal("bob", list[0].CreatedBy);
    }

    [Fact]
    public async Task GetMaxVersionNumber_is_zero_when_empty_then_tracks_max()
    {
        await TruncateAsync();

        Assert.Equal(0, await NewRepo().GetMaxVersionNumberAsync(RulesConfigType.Lookups));

        await NewRepo().AddVersionAsync(RulesConfigType.Lookups, 1, "{}", "alice", DateTime.UtcNow);
        Assert.Equal(1, await NewRepo().GetMaxVersionNumberAsync(RulesConfigType.Lookups));
    }

    [Fact]
    public async Task Versions_are_isolated_by_config_type()
    {
        await TruncateAsync();
        await NewRepo().AddVersionAsync(RulesConfigType.Rules, 1, "{\"rules\":true}", "a", DateTime.UtcNow);

        var lookups = await NewRepo().ListAsync(RulesConfigType.Lookups);
        Assert.Empty(lookups);
    }

    [Fact]
    public async Task AddAudit_writes_a_row()
    {
        await TruncateAsync();
        await NewRepo().AddAuditAsync("RulesConfig", "Rules", "Save", "alice", DateTime.UtcNow);

        await using var ctx = _fixture.CreateContext();
        Assert.Equal(1, ctx.AuditEntries.Count(a => a.EntityType == "RulesConfig" && a.Action == "Save"));
    }
}
```

> `RESTART IDENTITY` is omitted if the table names differ — but the EF default table name is the DbSet name (`RulesConfigVersions`, `AuditEntries`). Confirm against the generated migration; adjust the `TRUNCATE` names if the migration pluralised differently.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DfE.CheckPerformanceData.IntegrationTests --filter FullyQualifiedName~RulesConfigVersionRepositoryTests`
Expected: FAIL — compile error, `RulesConfigVersionRepository` does not exist.

- [ ] **Step 3: Implement the repository**

`src/DfE.CheckPerformanceData.Persistence/Repositories/RulesConfigVersionRepository.cs`:

```csharp
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class RulesConfigVersionRepository(IPortalDbContext context) : IRulesConfigVersionRepository
{
    public async Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default)
    {
        var max = await context.RulesConfigVersions
            .Where(v => v.ConfigType == type)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);
        return max ?? 0;
    }

    public async Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
        DateTime createdAt, CancellationToken ct = default)
    {
        context.RulesConfigVersions.Add(new RulesConfigVersion
        {
            ConfigType = type,
            VersionNumber = versionNumber,
            Content = content,
            CreatedBy = createdBy,
            CreatedAt = createdAt
        });
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default) =>
        await context.RulesConfigVersions
            .Where(v => v.ConfigType == type)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => ToDto(v))
            .ToListAsync(ct);

    public async Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await context.RulesConfigVersions
            .Where(v => v.Id == id)
            .Select(v => ToDto(v))
            .FirstOrDefaultAsync(ct);

    public async Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
        DateTime timestamp, CancellationToken ct = default)
    {
        context.AuditEntries.Add(new AuditEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            Timestamp = timestamp
        });
        await context.SaveChangesAsync(ct);
    }

    public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default) =>
        context.ExecuteInTransactionAsync(work, ct);

    private static RulesConfigVersionDto ToDto(RulesConfigVersion v) => new()
    {
        Id = v.Id,
        ConfigType = v.ConfigType,
        VersionNumber = v.VersionNumber,
        Content = v.Content,
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy
    };
}
```

> `AuditEntry` is in namespace `DfE.CheckPerformance.Persistence.Entities` (no `Data`) — keep both using directives exactly as shown.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/DfE.CheckPerformanceData.IntegrationTests --filter FullyQualifiedName~RulesConfigVersionRepositoryTests`
Expected: PASS (all four). (Testcontainers starts PostgreSQL automatically; Docker must be running.)

- [ ] **Step 5: Commit**

```bash
git add src/DfE.CheckPerformanceData.Persistence/Repositories/RulesConfigVersionRepository.cs tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/RulesConfigVersionRepositoryTests.cs
git commit -m "feat(rules): add RulesConfigVersionRepository"
```

---

## Task 6: `BlobRulesConfigStore` (Infrastructure adapter) + Azurite fixture

There is no Azurite test fixture yet. Add one mirroring `PostgresFixture`, using the `Testcontainers.Azurite` module so the test is hermetic.

**Files:**
- Modify: `tests/DfE.CheckPerformanceData.IntegrationTests/DfE.CheckPerformanceData.IntegrationTests.csproj`
- Create: `tests/DfE.CheckPerformanceData.IntegrationTests/Fixtures/AzuriteFixture.cs`
- Create: `src/DfE.CheckPerformanceData.Infrastructure/RulesEngine/BlobRulesConfigStore.cs`
- Test: `tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/BlobRulesConfigStoreTests.cs`

- [ ] **Step 1: Add the Testcontainers.Azurite package**

Run: `dotnet add tests/DfE.CheckPerformanceData.IntegrationTests package Testcontainers.Azurite`
Expected: package added (version resolved by `Directory.Packages.props` if centrally managed — if the build complains about an explicit version with central management, add the version to `src/Directory.Packages.props` instead and re-run with no version).

> If the Azurite image fails to pull from MCR with an EOF/timeout, it's the known IPv6 issue: toggle IPv6 off on the Wi-Fi adapter, pull `mcr.microsoft.com/azure-storage/azurite`, then toggle it back on.

- [ ] **Step 2: Create the Azurite fixture**

`tests/DfE.CheckPerformanceData.IntegrationTests/Fixtures/AzuriteFixture.cs`:

```csharp
using Testcontainers.Azurite;
using Xunit;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer _azurite = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    public string ConnectionString => _azurite.GetConnectionString();

    public async Task InitializeAsync() => await _azurite.StartAsync();

    public async Task DisposeAsync() => await _azurite.DisposeAsync();
}

[CollectionDefinition(nameof(AzuriteCollection))]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture> { }
```

- [ ] **Step 3: Write the failing integration test**

`tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/BlobRulesConfigStoreTests.cs`:

```csharp
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Options;
using Xunit;

namespace DfE.CheckPerformanceData.IntegrationTests.RulesConfig;

[Collection(nameof(AzuriteCollection))]
public sealed class BlobRulesConfigStoreTests(AzuriteFixture fixture)
{
    private readonly AzuriteFixture _fixture = fixture;

    private BlobRulesConfigStore CreateStore()
    {
        // Unique container per store instance keeps the two tests isolated.
        var options = new BlobRulesProviderOptions { RulesBlobContainer = $"rules-{Guid.NewGuid():N}" };
        return new BlobRulesConfigStore(new BlobServiceClient(_fixture.ConnectionString), Options.Create(options));
    }

    [Fact]
    public async Task Write_then_read_round_trips_content_and_returns_etag()
    {
        var store = CreateStore();

        await store.WriteAsync(RulesConfigType.Rules, "{\"version\":\"t1\"}", expectedETag: null);
        var read = await store.ReadAsync(RulesConfigType.Rules);

        Assert.Equal("{\"version\":\"t1\"}", read.Content);
        Assert.False(string.IsNullOrEmpty(read.ETag));
    }

    [Fact]
    public async Task Write_with_stale_etag_throws_conflict()
    {
        var store = CreateStore();
        await store.WriteAsync(RulesConfigType.Lookups, "{}", expectedETag: null);
        var first = await store.ReadAsync(RulesConfigType.Lookups);

        // A concurrent write moves the ETag on.
        await store.WriteAsync(RulesConfigType.Lookups, "{\"x\":1}", first.ETag);

        // Our now-stale ETag must be rejected.
        await Assert.ThrowsAsync<RulesConfigConflictException>(() =>
            store.WriteAsync(RulesConfigType.Lookups, "{\"y\":2}", first.ETag));
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/DfE.CheckPerformanceData.IntegrationTests --filter FullyQualifiedName~BlobRulesConfigStoreTests`
Expected: FAIL — compile error, `BlobRulesConfigStore` does not exist.

- [ ] **Step 5: Implement the store**

`src/DfE.CheckPerformanceData.Infrastructure/RulesEngine/BlobRulesConfigStore.cs`:

```csharp
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DfE.CheckPerformanceData.Application.RulesConfig;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Read/write access to the rules-config blobs for the admin editor. Reuses the same
/// container/blob names as <see cref="BlobRulesProvider"/> (via BlobRulesProviderOptions) so
/// the worker reads exactly what the editor writes. Writes use an ETag condition for optimistic
/// concurrency; pass null to create a not-yet-existing blob.
/// </summary>
public sealed class BlobRulesConfigStore : IRulesConfigStore
{
    private readonly BlobContainerClient _container;
    private readonly BlobRulesProviderOptions _options;

    public BlobRulesConfigStore(BlobServiceClient service, IOptions<BlobRulesProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(service);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _container = service.GetBlobContainerClient(_options.RulesBlobContainer);
    }

    private string BlobName(RulesConfigType type) =>
        type == RulesConfigType.Rules ? _options.RulesBlobName : _options.LookupsBlobName;

    public async Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default)
    {
        var client = _container.GetBlobClient(BlobName(type));
        var response = await client.DownloadContentAsync(ct).ConfigureAwait(false);
        var content = response.Value.Content?.ToString() ?? string.Empty;
        var etag = response.Value.Details.ETag.ToString();
        return new RulesConfigBlob(content, etag);
    }

    public async Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        var client = _container.GetBlobClient(BlobName(type));

        var conditions = string.IsNullOrEmpty(expectedETag)
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }     // create-only: fail if it already exists
            : new BlobRequestConditions { IfMatch = new ETag(expectedETag) };

        var uploadOptions = new BlobUploadOptions
        {
            Conditions = conditions,
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        try
        {
            await client.UploadAsync(stream, uploadOptions, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 409)
        {
            throw new RulesConfigConflictException(
                $"The {type} config was changed by someone else since it was loaded. Reload and re-apply your changes.");
        }
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/DfE.CheckPerformanceData.IntegrationTests --filter FullyQualifiedName~BlobRulesConfigStoreTests`
Expected: PASS (both). (Testcontainers starts Azurite automatically; Docker must be running.)

- [ ] **Step 7: Commit**

```bash
git add tests/DfE.CheckPerformanceData.IntegrationTests/DfE.CheckPerformanceData.IntegrationTests.csproj tests/DfE.CheckPerformanceData.IntegrationTests/Fixtures/AzuriteFixture.cs tests/DfE.CheckPerformanceData.IntegrationTests/RulesConfig/BlobRulesConfigStoreTests.cs src/DfE.CheckPerformanceData.Infrastructure/RulesEngine/BlobRulesConfigStore.cs src/Directory.Packages.props
git commit -m "feat(rules): add BlobRulesConfigStore with ETag concurrency + Azurite test fixture"
```

---

## Task 7: `RulesConfigService` (orchestration)

The heart of the foundation: read, save (validate → concurrency → blob write → version snapshot → audit), list versions, roll back. Mirrors `ContentBlockService`'s transaction + versioning shape. Unit-tested with hand-written fakes (no mocking library).

**Files:**
- Create: `RulesConfigSaveResult.cs`, `IRulesConfigService.cs`, `RulesConfigService.cs` (under `Application/RulesConfig/`)
- Test: `tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RulesConfigServiceTests.cs`

- [ ] **Step 1: Save-result type**

`RulesConfigSaveResult.cs`:

```csharp
namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Outcome of a save attempt. Validation failures carry the error list for a GOV.UK error
/// summary; nothing is persisted in that case.
/// </summary>
public sealed record RulesConfigSaveResult(bool Saved, int? VersionNumber, IReadOnlyList<string> Errors)
{
    public static RulesConfigSaveResult Success(int versionNumber) => new(true, versionNumber, Array.Empty<string>());
    public static RulesConfigSaveResult Invalid(IReadOnlyList<string> errors) => new(false, null, errors);
}
```

- [ ] **Step 2: Service interface**

`IRulesConfigService.cs`:

```csharp
using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>Read/edit/version the rules and lookups configs for the admin editor.</summary>
public interface IRulesConfigService
{
    Task<(RuleSet Rules, string? ETag)> GetRulesAsync(CancellationToken ct = default);
    Task<(Lookups Lookups, string? ETag)> GetLookupsAsync(CancellationToken ct = default);
    Task<RulesConfigSaveResult> SaveRulesAsync(RuleSet rules, string? expectedETag, CancellationToken ct = default);
    Task<RulesConfigSaveResult> SaveLookupsAsync(Lookups lookups, string? expectedETag, CancellationToken ct = default);
    Task<IReadOnlyList<RulesConfigVersionDto>> ListVersionsAsync(RulesConfigType type, CancellationToken ct = default);
    Task<RulesConfigSaveResult> RollbackAsync(int versionId, string? expectedETag, CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the failing service tests**

`tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RulesConfigServiceTests.cs`:

```csharp
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using Xunit;

namespace DfE.CheckPerformanceData.UnitTests.RulesConfig;

public class RulesConfigServiceTests
{
    private sealed class FakeStore : IRulesConfigStore
    {
        public Dictionary<RulesConfigType, string> Content = new();
        public string CurrentETag = "etag-0";
        public int Writes;
        public string? LastExpectedETag = "UNSET";

        public Task<RulesConfigBlob> ReadAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult(new RulesConfigBlob(Content.GetValueOrDefault(type, ""), CurrentETag));

        public Task WriteAsync(RulesConfigType type, string content, string? expectedETag, CancellationToken ct = default)
        {
            LastExpectedETag = expectedETag;
            Content[type] = content;
            Writes++;
            CurrentETag = $"etag-{Writes}";
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepo : IRulesConfigVersionRepository
    {
        public List<RulesConfigVersionDto> Versions = new();
        public List<string> Audits = new();
        private int _nextId = 1;

        public Task<int> GetMaxVersionNumberAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult(Versions.Where(v => v.ConfigType == type).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max());

        public Task AddVersionAsync(RulesConfigType type, int versionNumber, string content, string? createdBy,
            DateTime createdAt, CancellationToken ct = default)
        {
            Versions.Add(new RulesConfigVersionDto
            {
                Id = _nextId++, ConfigType = type, VersionNumber = versionNumber,
                Content = content, CreatedBy = createdBy, CreatedAt = createdAt
            });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RulesConfigVersionDto>> ListAsync(RulesConfigType type, CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<RulesConfigVersionDto>)Versions
                .Where(v => v.ConfigType == type).OrderByDescending(v => v.VersionNumber).ToList());

        public Task<RulesConfigVersionDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Id == id));

        public Task AddAuditAsync(string entityType, string entityId, string action, string? userId,
            DateTime timestamp, CancellationToken ct = default)
        {
            Audits.Add($"{entityType}:{entityId}:{action}:{userId}");
            return Task.CompletedTask;
        }

        public Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken ct = default) => work();
    }

    private sealed class FakeUser : ICurrentUserService
    {
        public string UserId => "user-1";
        public string DisplayName => "Alice Admin";
        public string OrganisationId => "org";
        public string OrganisationName => "Org";
        public string OrganisationUrn => "1";
    }

    private static RuleSet ValidRules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        new OutcomeRules("Deceased", "Deceased",
            new[] { new RuleBranch("DEC-1", DecisionStatus.AutoApproved, Predicate.Otherwise.Instance) })
    });

    private static RuleSet InvalidRules() => new("v1", DateTimeOffset.UnixEpoch, new[]
    {
        // final branch is NOT 'otherwise' -> validator fails
        new OutcomeRules("Bad", "Bad",
            new[] { new RuleBranch("B1", DecisionStatus.AutoApproved, new Predicate.FieldEq("keyStage", new FieldValue.Str("KS4"))) })
    });

    private static RulesConfigService NewService(FakeStore store, FakeRepo repo) =>
        new(store, repo, new RuleSetValidator(), new LookupsValidator(), new FakeUser(), TimeProvider.System);

    [Fact]
    public async Task SaveRules_valid_writes_blob_versions_and_audits()
    {
        var store = new FakeStore { CurrentETag = "etag-A" };
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.SaveRulesAsync(ValidRules(), expectedETag: "etag-A");

        Assert.True(result.Saved);
        Assert.Equal(1, result.VersionNumber);
        Assert.Equal(1, store.Writes);
        Assert.Equal("etag-A", store.LastExpectedETag);     // concurrency token forwarded
        Assert.Single(repo.Versions);
        Assert.Equal("Alice Admin", repo.Versions[0].CreatedBy);
        Assert.Single(repo.Audits);

        var written = JsonSerializer.Deserialize<RuleSet>(store.Content[RulesConfigType.Rules], RulesJson.Options)!;
        Assert.Equal("Deceased", written.Outcomes[0].Key);
    }

    [Fact]
    public async Task SaveRules_invalid_writes_nothing_and_returns_errors()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        var result = await svc.SaveRulesAsync(InvalidRules(), expectedETag: "etag-0");

        Assert.False(result.Saved);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, store.Writes);
        Assert.Empty(repo.Versions);
        Assert.Empty(repo.Audits);
    }

    [Fact]
    public async Task SaveRules_increments_version_number()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        await svc.SaveRulesAsync(ValidRules(), null);
        var second = await svc.SaveRulesAsync(ValidRules(), store.CurrentETag);

        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(new[] { 1, 2 }, repo.Versions.Select(v => v.VersionNumber).OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task Rollback_revalidates_and_writes_a_new_version()
    {
        var store = new FakeStore();
        var repo = new FakeRepo();
        var svc = NewService(store, repo);

        await svc.SaveRulesAsync(ValidRules(), null);            // version 1
        var v1 = Assert.Single(repo.Versions);

        var result = await svc.RollbackAsync(v1.Id, store.CurrentETag);

        Assert.True(result.Saved);
        Assert.Equal(2, result.VersionNumber);                  // rollback is a NEW version
        Assert.Equal(2, repo.Versions.Count);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~RulesConfigServiceTests`
Expected: FAIL — compile error, `RulesConfigService` does not exist.

- [ ] **Step 5: Implement the service**

`src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigService.cs`:

```csharp
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;

namespace DfE.CheckPerformanceData.Application.RulesConfig;

public sealed class RulesConfigService(
    IRulesConfigStore store,
    IRulesConfigVersionRepository versions,
    RuleSetValidator rulesValidator,
    LookupsValidator lookupsValidator,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IRulesConfigService
{
    public async Task<(RuleSet Rules, string? ETag)> GetRulesAsync(CancellationToken ct = default)
    {
        var blob = await store.ReadAsync(RulesConfigType.Rules, ct);
        var rules = JsonSerializer.Deserialize<RuleSet>(blob.Content, RulesJson.Options)
            ?? throw new InvalidOperationException("rules.json deserialised to null.");
        return (rules, blob.ETag);
    }

    public async Task<(Lookups Lookups, string? ETag)> GetLookupsAsync(CancellationToken ct = default)
    {
        var blob = await store.ReadAsync(RulesConfigType.Lookups, ct);
        return (DeserialiseLookups(blob.Content), blob.ETag);
    }

    public async Task<RulesConfigSaveResult> SaveRulesAsync(RuleSet rules, string? expectedETag, CancellationToken ct = default)
    {
        var validation = rulesValidator.Validate(rules);
        if (!validation.IsValid)
        {
            return RulesConfigSaveResult.Invalid(validation.Errors);
        }

        var stamped = validation.ResolvedRules! with
        {
            Version = NewVersionString(),
            UpdatedAt = timeProvider.GetUtcNow()
        };
        var json = JsonSerializer.Serialize(stamped, RulesJson.Options);
        return await PersistAsync(RulesConfigType.Rules, json, expectedETag, ct);
    }

    public async Task<RulesConfigSaveResult> SaveLookupsAsync(Lookups lookups, string? expectedETag, CancellationToken ct = default)
    {
        var validation = lookupsValidator.Validate(lookups);
        if (!validation.IsValid)
        {
            return RulesConfigSaveResult.Invalid(validation.Errors);
        }

        var json = SerialiseLookups(lookups);
        return await PersistAsync(RulesConfigType.Lookups, json, expectedETag, ct);
    }

    public Task<IReadOnlyList<RulesConfigVersionDto>> ListVersionsAsync(RulesConfigType type, CancellationToken ct = default) =>
        versions.ListAsync(type, ct);

    public async Task<RulesConfigSaveResult> RollbackAsync(int versionId, string? expectedETag, CancellationToken ct = default)
    {
        var snapshot = await versions.GetByIdAsync(versionId, ct)
            ?? throw new InvalidOperationException($"Rules config version {versionId} not found.");

        if (snapshot.ConfigType == RulesConfigType.Rules)
        {
            var rules = JsonSerializer.Deserialize<RuleSet>(snapshot.Content, RulesJson.Options)
                ?? throw new InvalidOperationException("Stored rules snapshot deserialised to null.");
            return await SaveRulesAsync(rules, expectedETag, ct);
        }

        return await SaveLookupsAsync(DeserialiseLookups(snapshot.Content), expectedETag, ct);
    }

    // Write blob, snapshot version, audit — together. A blob conflict thrown first aborts the
    // transaction so no orphan version/audit row is written.
    private async Task<RulesConfigSaveResult> PersistAsync(RulesConfigType type, string json, string? expectedETag, CancellationToken ct)
    {
        var nextVersion = await versions.GetMaxVersionNumberAsync(type, ct) + 1;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var who = string.IsNullOrWhiteSpace(currentUser.DisplayName) ? currentUser.UserId : currentUser.DisplayName;

        await versions.ExecuteInTransactionAsync(async () =>
        {
            await store.WriteAsync(type, json, expectedETag, ct);
            await versions.AddVersionAsync(type, nextVersion, json, who, now, ct);
            await versions.AddAuditAsync("RulesConfig", type.ToString(), "Save", currentUser.UserId, now, ct);
        }, ct);

        return RulesConfigSaveResult.Success(nextVersion);
    }

    private string NewVersionString() => timeProvider.GetUtcNow().ToString("yyyy.MM.dd-HHmmss");

    private static Lookups DeserialiseLookups(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return Lookups.Empty;
        var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(content, RulesJson.Options);
        return raw is null
            ? Lookups.Empty
            : new Lookups(raw.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<string>)kvp.Value.ToArray()));
    }

    private static string SerialiseLookups(Lookups lookups)
    {
        var raw = lookups.CountryLanguages.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        return JsonSerializer.Serialize(raw, RulesJson.Options);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests --filter FullyQualifiedName~RulesConfigServiceTests`
Expected: PASS (all four).

- [ ] **Step 7: Commit**

```bash
git add src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigSaveResult.cs src/DfE.CheckPerformanceData.Application/RulesConfig/IRulesConfigService.cs src/DfE.CheckPerformanceData.Application/RulesConfig/RulesConfigService.cs tests/DfE.CheckPerformanceData.UnitTests/RulesConfig/RulesConfigServiceTests.cs
git commit -m "feat(rules): add RulesConfigService orchestration (validate, version, audit, rollback)"
```

---

## Task 8: Dependency injection wiring

**Files:**
- Modify: `src/DfE.CheckPerformanceData.Application/DependencyManager.cs`
- Modify: the Persistence DI registration (where `IContentBlockRepository` → `ContentBlockRepository` is registered — find with `grep`)
- Modify: `src/DfE.CheckPerformanceData.Web/Program.cs`

- [ ] **Step 1: Application DI**

In `AddApplicationDependencies` (near the rules-engine registrations) add:

```csharp
        services.AddSingleton<RulesConfig.LookupsValidator>();
        services.AddScoped<RulesConfig.IRulesConfigService, RulesConfig.RulesConfigService>();
```

- [ ] **Step 2: Persistence DI**

Run: `grep -rn "IContentBlockRepository" src/DfE.CheckPerformanceData.Persistence`

In that same registration method, add:

```csharp
        services.AddScoped<DfE.CheckPerformanceData.Application.RulesConfig.IRulesConfigVersionRepository,
            DfE.CheckPerformanceData.Persistence.Repositories.RulesConfigVersionRepository>();
```

- [ ] **Step 3: Web host DI**

In `src/DfE.CheckPerformanceData.Web/Program.cs`, after the `BlobServiceClient` registration (~line 125), add:

```csharp
    builder.Services.Configure<DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions>(
        builder.Configuration.GetSection(
            DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions.SectionName));
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddScoped<
        DfE.CheckPerformanceData.Application.RulesConfig.IRulesConfigStore,
        DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesConfigStore>();
```

Add `using Microsoft.Extensions.DependencyInjection.Extensions;` to Program.cs if `TryAddSingleton` is not resolvable. The `BlobRulesProviderOptions` defaults (`rules-config`, `rules.json`, `country-languages.json`) apply when the `RulesEngineOptions` config section is absent in the Web host.

- [ ] **Step 4: Build everything**

Run: `dotnet build`
Expected: Build succeeded across all projects, 0 errors.

- [ ] **Step 5: Resolution smoke check**

Run: `dotnet run --project src/DfE.CheckPerformanceData.Web` then Ctrl-C once it logs "Now listening on…".
Expected: app starts with no DI resolution errors. (`IRulesConfigService` is exercised by the controller in M2; a clean start is sufficient here.)

- [ ] **Step 6: Full suites**

Run: `dotnet test tests/DfE.CheckPerformanceData.UnitTests`
Run: `dotnet test tests/DfE.CheckPerformanceData.IntegrationTests`
Expected: PASS (new tests green, no regressions).

- [ ] **Step 7: Commit**

```bash
git add src/DfE.CheckPerformanceData.Application/DependencyManager.cs src/DfE.CheckPerformanceData.Web/Program.cs src/DfE.CheckPerformanceData.Persistence/
git commit -m "feat(rules): wire up RulesConfig DI (service, repository, blob store)"
```

---

## Milestone 1 — definition of done

- `RuleSetValidator` rejects duplicate outcome keys; `LookupsValidator` validates the country map.
- `RulesConfigVersion` table exists (migration applied) with a unique `(ConfigType, VersionNumber)` index.
- `RulesConfigService` reads both configs (with ETag), saves with validate→ETag-guard→blob-write→version-snapshot→audit, lists versions, and rolls back (re-validated, as a new version).
- All wired into DI; `dotnet build` and both test suites pass.
- No UI yet — that is Milestone 2.

When this is green, request the **Milestone 2** plan (read-only admin surface).
