using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class ContentBlockRepository(IPortalDbContext context) : IContentBlockRepository
{
    // Queries — use ProjectToDto so EF translates the mapping to SQL

    public async Task<List<ContentBlockDto>> GetAllAsync() =>
        await context.ContentBlocks
            .AsNoTracking()
            .OrderBy(b => b.Key)
            .ProjectToDto()
            .ToListAsync();

    public async Task<ContentBlockDto?> GetByKeyAsync(string key) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.Key == key)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<List<ContentBlockDto>> SearchAsync(string query, int take)
    {
        // Full-text ranked search over the Keywords (A) + ValuePlainText (B) vector. Exclude
        // e2e seed blocks and the guidance nav block (it lists every section title).
        // websearch_to_tsquery tolerates any input; sanitisation is done upstream via the
        // short-query short-circuit in SiteSearchService / ContentBlockSearchService.
        //
        // Whitespace between plain words is turned into OR so "merge booga" doesn't hit zero
        // when "booga" isn't in the corpus. Queries that already use websearch operators
        // (OR / "phrase" / -negation) are passed through untouched.
        //
        // Manual Select (not ProjectToDto) because ContentBlockDto.Rank needs the row's
        // ts_rank result and Mapperly can't compose that with the entity mapping.
        //
        // The widened projection surfaces every tsquery-matching row — kept rows carry
        // ExcludedBy = null, dropped rows carry a slug identifying which silent filter
        // discarded them ("e2e-key", "guidance-ks4-2026-nav-key",
        // "contentblock-appearinsearch-false"). Two per-field ts_rank columns (Keywords
        // and Value) ride alongside the combined Rank so downstream telemetry can answer
        // "why did this rank above that". The three .Where filters that used to drop rows
        // become CASE branches; the FTS predicate stays as WHERE. Two subqueries with
        // independent Take values (take for kept, take*3 for excluded) UNION-ALL together
        // so a high-exclusion corpus can't starve exclusion visibility.
        var normalisedQuery = Application.Search.SearchTermNormalizer.OrJoinWhitespace(query);

        var widened = context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", normalisedQuery)))
            .Select(b => new
            {
                Block = b,
                // Combined Rank — unchanged from Phase 1.06.
                Rank = b.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", normalisedQuery)),
                // Per-field ranks — additive projections; every WebSearchToTsQuery call
                // site inlined (its config arg is [NotParameterized]; hoisting throws).
                RankKeywords = (float?)EF.Functions.ToTsVector("english", b.Keywords ?? string.Empty)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedQuery)),
                RankValue = (float?)EF.Functions.ToTsVector("english", b.ValuePlainText)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedQuery)),
                // Chained ternary translates to CASE WHEN. Precedence matches Phase 1.06's
                // implicit ordering: key-prefix filter runs before the AppearInSearch check.
                ExcludedBy = b.Key.StartsWith("e2e-")
                    ? "e2e-key"
                    : b.Key == "guidance-ks4-2026-nav"
                        ? "guidance-ks4-2026-nav-key"
                        : !b.AppearInSearch
                            ? "contentblock-appearinsearch-false"
                            : (string?)null,
            });

        // Kept rows LIMITed at the user-facing count with the shipped Phase 1.06 ORDER BY.
        var keptQuery = widened
            .Where(x => x.ExcludedBy == null)
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Block.Key)  // stable tie-break
            .Take(take)
            .Select(x => new ContentBlockDto
            {
                Id = x.Block.Id,
                ContentId = x.Block.ContentId,
                Key = x.Block.Key,
                BlockType = x.Block.BlockType,
                Value = x.Block.Value,
                LastSeenPath = x.Block.LastSeenPath,
                LastSeenAt = x.Block.LastSeenAt,
                AppearInSearch = x.Block.AppearInSearch,
                Keywords = x.Block.Keywords,
                Rank = x.Rank,
                RankKeywords = x.RankKeywords,
                RankValue = x.RankValue,
                ExcludedBy = x.ExcludedBy,
                CreatedAt = x.Block.CreatedAt,
                UpdatedAt = x.Block.UpdatedAt,
            });

        // Excluded rows soft-capped at take * 3 — visibility for telemetry without a
        // runaway scan when a corpus has thousands of hidden-from-search rows. Same ORDER
        // BY as the kept subquery so the top-ranked exclusions surface first (most
        // diagnostically interesting: rows that came close to being a hit) and the subset
        // is deterministic between runs.
        var excludedQuery = widened
            .Where(x => x.ExcludedBy != null)
            .OrderByDescending(x => x.Rank)
            .ThenBy(x => x.Block.Key)
            .Take(take * 3)
            .Select(x => new ContentBlockDto
            {
                Id = x.Block.Id,
                ContentId = x.Block.ContentId,
                Key = x.Block.Key,
                BlockType = x.Block.BlockType,
                Value = x.Block.Value,
                LastSeenPath = x.Block.LastSeenPath,
                LastSeenAt = x.Block.LastSeenAt,
                AppearInSearch = x.Block.AppearInSearch,
                Keywords = x.Block.Keywords,
                Rank = x.Rank,
                RankKeywords = x.RankKeywords,
                RankValue = x.RankValue,
                ExcludedBy = x.ExcludedBy,
                CreatedAt = x.Block.CreatedAt,
                UpdatedAt = x.Block.UpdatedAt,
            });

        // LINQ Concat translates to SQL UNION ALL — kept rows first (up to take), then the
        // excluded rows (up to take * 3). Downstream consumers filter ExcludedBy != null
        // out of user-facing surfaces and into telemetry.
        return await keptQuery.Concat(excludedQuery).ToListAsync();
    }

    public async Task<ContentBlockDto?> GetByContentIdAsync(Guid contentId) =>
        await context.ContentBlocks
            .AsNoTracking()
            .Where(b => b.ContentId == contentId)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<int> GetMaxVersionNumberAsync(int contentBlockId) =>
        await context.ContentBlockVersions
            .Where(v => v.ContentBlockId == contentBlockId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

    public async Task<ContentBlockVersionDto?> GetVersionByIdAsync(int versionId) =>
        await context.ContentBlockVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId)
            .ProjectToVersionDto()
            .FirstOrDefaultAsync();

    public async Task<List<ContentBlockVersionDto>> GetVersionsByKeyAsync(string key) =>
        await context.ContentBlockVersions
            .AsNoTracking()
            .Where(v => v.ContentBlock.Key == key)
            .OrderByDescending(v => v.VersionNumber)
            .ProjectToVersionDto()
            .ToListAsync();

    // Commands — work with tracked entities internally

    public async Task<ContentBlockDto> AddBlockAsync(string key, string blockType, string value, string valuePlainText, Guid? contentId = null, bool appearInSearch = true, string? keywords = null)
    {
        var entity = new ContentBlock
        {
            Key = key,
            BlockType = blockType,
            Value = value,
            ValuePlainText = valuePlainText,
            AppearInSearch = appearInSearch,
            Keywords = keywords,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Preserve a supplied cross-environment identity; otherwise the DB default generates one.
        if (contentId is { } id && id != Guid.Empty)
            entity.ContentId = id;

        context.ContentBlocks.Add(entity);
        await context.SaveChangesAsync();

        return ContentBlockMapper.ToDto(entity);
    }

    // Content-staging Replace: overwrite an existing block in place — including a Key/type change
    // (a "rename") and reconciling its cross-environment identity — matched by row id.
    public async Task UpdateForStagingAsync(int id, string key, string blockType, string value, string valuePlainText, Guid contentId, bool appearInSearch, string? keywords)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Key = key;
        entity.BlockType = blockType;
        entity.Value = value;
        entity.ValuePlainText = valuePlainText;
        entity.AppearInSearch = appearInSearch;
        entity.Keywords = keywords;
        if (contentId != Guid.Empty)
            entity.ContentId = contentId;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task AddVersionAsync(int contentBlockId, string value, int versionNumber)
    {
        var version = new ContentBlockVersion
        {
            ContentBlockId = contentBlockId,
            Value = value,
            VersionNumber = versionNumber,
            CreatedAt = DateTime.UtcNow
        };

        context.ContentBlockVersions.Add(version);
        await context.SaveChangesAsync();
    }

    // Records where a block was last rendered. Called from the editable view components only when
    // the path actually changed, so this rarely writes; matched by Key (a no-op if the block has
    // no saved row yet).
    public async Task SetLastSeenAsync(string key, string path, DateTime seenAt)
    {
        var entity = await context.ContentBlocks.FirstOrDefaultAsync(b => b.Key == key);
        if (entity is null) return;

        entity.LastSeenPath = path;
        entity.LastSeenAt = seenAt;
        await context.SaveChangesAsync();
    }

    public async Task UpdateValueAsync(int id, string newValue, string newValuePlainText)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Value = newValue;
        entity.ValuePlainText = newValuePlainText;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task SetAppearInSearchAsync(int id, bool appearInSearch)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.AppearInSearch = appearInSearch;
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task SetKeywordsAsync(int id, string? keywords)
    {
        var entity = await context.ContentBlocks.FindAsync(id)
            ?? throw new InvalidOperationException($"Content block {id} not found.");

        entity.Keywords = string.IsNullOrWhiteSpace(keywords) ? null : keywords.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task SaveChangesAsync() =>
        await context.SaveChangesAsync();

    public async Task ExecuteInTransactionAsync(Func<Task> work) =>
        await context.ExecuteInTransactionAsync(work);
}
