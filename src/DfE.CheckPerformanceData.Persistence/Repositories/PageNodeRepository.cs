using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class PageNodeRepository(IPortalDbContext context) : IPageNodeRepository
{
    public Task<List<PageNodeTreeItemDto>> GetTreeAsync() =>
        context.PageNodes
            .AsNoTracking()
            .OrderBy(n => n.ParentId)
            .ThenBy(n => n.SortOrder)
            .ThenBy(n => n.CreatedDate)
            .Select(n => new PageNodeTreeItemDto
            {
                Id = n.Id,
                ParentId = n.ParentId,
                Segment = n.Segment,
                Path = n.Path,
                SortOrder = n.SortOrder,
                CreatedDate = n.CreatedDate,
                Title = n.Title,
                Subtitle = n.Subtitle,
                PageName = n.PageName,
                PageType = n.PageType,
                ShowInMenu = n.ShowInMenu,
                AppearInSearch = n.AppearInSearch,
                Keywords = n.Keywords,
                HasLiveVersion = n.Versions.Any(v => v.IsCurrent)
            })
            .ToListAsync();

    public Task<PageNodeDto?> GetByPathAsync(string path) =>
        context.PageNodes
            .AsNoTracking()
            .Where(n => n.Path == path)
            .Select(NodeProjection)
            .FirstOrDefaultAsync();

    public Task<PublishedPageInfoDto?> GetPublishedByPathAsync(string path) =>
        context.PageNodes
            .AsNoTracking()
            // Skip pages the editor has toggled out of search — the content-block resolver
            // shouldn't turn a "hide from search" page into a search destination via a block hit.
            .Where(n => n.Path == path && n.DeletedDate == null && n.AppearInSearch)
            .Where(n => n.Versions.Any(v => v.IsCurrent))
            .Select(n => new PublishedPageInfoDto(n.Path, n.Title))
            .FirstOrDefaultAsync();

    public async Task<List<PageSearchHitRaw>> SearchPagesAsync(string term, string? scopePath, int max)
    {
        var primary = await SearchPagesOnceAsync(term, scopePath, max);

        // Zero-result hyphen fallback: if the primary websearch_to_tsquery run returned no
        // KEPT rows and the term contains a hyphen (and isn't operator-only), retry once
        // with hyphens replaced by spaces. Mirrors the SiteSearchService.SearchAsync fallback
        // so direct repository callers get the same URL-slug matching (websearch_to_tsquery
        // on a hyphenated slug emits a compound-lexeme phrase clause that misses the
        // per-word tsvector; the space-normalised retry catches those). Zero cost when the
        // primary already matched; exempts operator-bearing inputs so an explicit -negation
        // or "phrase" isn't second-guessed.
        var hasKept = false;
        for (int i = 0; i < primary.Count; i++)
        {
            if (primary[i].ExcludedBy is null) { hasKept = true; break; }
        }

        if (!hasKept
            && !string.IsNullOrEmpty(term)
            && term.Contains('-')
            && !SearchTermNormalizer.ContainsWebSearchOperator(term))
        {
            return await SearchPagesOnceAsync(term.Replace('-', ' '), scopePath, max);
        }

        return primary;
    }

    private Task<List<PageSearchHitRaw>> SearchPagesOnceAsync(string term, string? scopePath, int max)
    {
        // Full-text ranked search across two vectors — PageNode.SearchVector (Keywords A, Title
        // B, Subtitle C) and PageNodeVersion.SearchVector (BodyPlainText D). A row matches if
        // either vector matches; the two ts_rank scores are summed so a page with a keyword hit
        // AND a body hit outranks either alone.
        //
        // websearch_to_tsquery NEVER raises syntax errors on user input — accepts empty strings,
        // punctuation, unbalanced operators. Sanitisation is not required here; short/empty
        // terms are guarded upstream in SiteSearchService.
        //
        // EF.Functions.WebSearchToTsQuery MUST appear inline inside each expression tree that
        // uses it — its `config` argument is [NotParameterized], so the Npgsql translator only
        // recognises direct call sites. Hoisting to a local forces client-evaluation and throws.
        //
        // Whitespace between plain words is turned into OR so "merge booga" doesn't hit zero
        // when "booga" isn't in the corpus. Queries that already use websearch operators
        // (OR / "phrase" / -negation) are passed through untouched.
        //
        // The widened projection surfaces every tsquery-matching row — kept rows carry
        // ExcludedBy = null, dropped rows carry a slug identifying which silent filter
        // discarded them. Four per-field ts_rank columns (Keywords / Title / Subtitle / Body)
        // ride alongside the combined RankTotal so downstream telemetry can answer "why
        // did this rank above that". Filter WHERE clauses that used to hide rows —
        // AppearInSearch = false pages and pages without a live version — become CASE
        // branches; structural filters (DeletedDate, PageType != "folder", scope path)
        // stay as WHERE clauses. Two subqueries with independent Take values (max for
        // kept, max * 3 for excluded) UNION-ALL together so a high-exclusion corpus
        // can't starve exclusion visibility. Note RankBody / BodyPlainText are absent on
        // draft-excluded rows because the excluded reason is precisely the missing live
        // version — the projection substitutes null / empty-string so downstream code
        // never NREs.
        var normalisedTerm = SearchTermNormalizer.OrJoinWhitespace(term);
        var scopePrefix = string.IsNullOrEmpty(scopePath) ? null : scopePath + "/";

        var widened = context.PageNodes
            .AsNoTracking()
            .Where(n => n.DeletedDate == null)
            // Folder pages are containers only — nothing to render, nothing to link to.
            .Where(n => n.PageType != "folder")
            .Where(n => scopePath == null || n.Path == scopePath || n.Path.StartsWith(scopePrefix!))
            .Select(n => new
            {
                Node = n,
                Live = n.Versions
                    .Where(v => v.IsCurrent)
                    .Select(v => new { v.BodyPlainText, v.SearchVector })
                    .FirstOrDefault()
            })
            // Only rows the tsquery actually matched — the CASE tags kept-vs-excluded WITHIN
            // that set. Live is null-guarded so draft-only pages that match on the Node
            // vector still surface (tagged draft-page below).
            .Where(x =>
                x.Node.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", normalisedTerm))
                || (x.Live != null
                    && x.Live.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", normalisedTerm))))
            .Select(x => new
            {
                x.Node,
                x.Live,
                // RankTotal — sum of the two vectors when Live exists, Node vector alone for
                // draft-page rows. The draft branch only fires for excluded rows, so kept-row
                // ordering is unchanged.
                RankTotal = x.Live != null
                    ? x.Node.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm))
                      + x.Live.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm))
                    : x.Node.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                // Per-field ranks — additive projections; do not feed ORDER BY. Every call
                // site inlines EF.Functions.WebSearchToTsQuery (its config arg is
                // [NotParameterized]; hoisting throws at translation time).
                RankKeywords = (float?)EF.Functions.ToTsVector("english", x.Node.Keywords ?? string.Empty)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankTitle = (float?)EF.Functions.ToTsVector("english", x.Node.Title)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankSubtitle = (float?)EF.Functions.ToTsVector("english", x.Node.Subtitle ?? string.Empty)
                    .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm)),
                RankBody = x.Live != null
                    ? (float?)EF.Functions.ToTsVector("english", x.Live.BodyPlainText)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", normalisedTerm))
                    : null,
                // Chained ternary translates to CASE WHEN. WHERE precedence: AppearInSearch
                // checked first, then Live != null.
                ExcludedBy = !x.Node.AppearInSearch
                    ? "pagenode-appearinsearch-false"
                    : (x.Live == null ? "draft-page" : (string?)null),
            });

        // Kept rows LIMITed at the user-facing count with the shipped ORDER BY.
        var keptQuery = widened
            .Where(x => x.ExcludedBy == null)
            .OrderByDescending(x => x.RankTotal)
            .ThenBy(x => x.Node.Path)  // stable tie-break
            .Take(max)
            .Select(x => new PageSearchHitRaw
            {
                PageId = x.Node.Id,
                Path = x.Node.Path,
                Title = x.Node.Title,
                Subtitle = x.Node.Subtitle,
                BodyPlainText = x.Live!.BodyPlainText,
                Rank = x.RankTotal,
                RankKeywords = x.RankKeywords,
                RankTitle = x.RankTitle,
                RankSubtitle = x.RankSubtitle,
                RankBody = x.RankBody,
                ExcludedBy = x.ExcludedBy,
            });

        // Excluded rows soft-capped at max * 3 — visibility for telemetry without a runaway
        // scan when a corpus has thousands of hidden-from-search rows. Same ORDER BY as
        // the kept subquery so the top-ranked exclusions surface first (most diagnostically
        // interesting: rows that came close to being a hit) and the subset is deterministic
        // between runs.
        var excludedQuery = widened
            .Where(x => x.ExcludedBy != null)
            .OrderByDescending(x => x.RankTotal)
            .ThenBy(x => x.Node.Path)
            .Take(max * 3)
            .Select(x => new PageSearchHitRaw
            {
                PageId = x.Node.Id,
                Path = x.Node.Path,
                Title = x.Node.Title,
                Subtitle = x.Node.Subtitle,
                BodyPlainText = x.Live != null ? x.Live.BodyPlainText : string.Empty,
                Rank = x.RankTotal,
                RankKeywords = x.RankKeywords,
                RankTitle = x.RankTitle,
                RankSubtitle = x.RankSubtitle,
                RankBody = x.RankBody,
                ExcludedBy = x.ExcludedBy,
            });

        // LINQ Concat translates to SQL UNION ALL — kept rows first (up to max), then the
        // excluded rows (up to max * 3). Downstream consumers filter ExcludedBy != null out
        // of user-facing surfaces and into telemetry.
        return keptQuery.Concat(excludedQuery).ToListAsync();
    }

    public Task<PageNodeDto?> GetByIdAsync(Guid id) =>
        context.PageNodes
            .AsNoTracking()
            .Where(n => n.Id == id)
            .Select(NodeProjection)
            .FirstOrDefaultAsync();

    public async Task<PageNodeDto> CreateNodeAsync(
        Guid? parentId, string segment, string path, string title, string pageType, string? userId)
    {
        var now = DateTime.UtcNow;

        // New sibling gets max(existing sibling SortOrder) + 1, so siblings always have distinct
        // increasing orders and swap/position-reassign operations produce visible changes.
        var maxOrder = await context.PageNodes
            .Where(n => n.ParentId == parentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync() ?? -1;

        var entity = new PageNode
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Segment = segment,
            Path = path,
            SortOrder = maxOrder + 1,
            Title = title,
            PageType = pageType,
            CreatedDate = now,
            UpdatedDate = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        context.PageNodes.Add(entity);
        await context.SaveChangesAsync();
        return ToNodeDto(entity);
    }

    public async Task<int> AddVersionAsync(
        Guid nodeId, string content, string bodyPlainText,
        DateTime? publishFrom, DateTime? publishTo, string? userId)
    {
        var max = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .MaxAsync(v => (int?)v.VersionId) ?? 0;
        var versionId = max + 1;

        context.PageNodeVersions.Add(new PageNodeVersion
        {
            Id = Guid.NewGuid(),
            PageNodeId = nodeId,
            VersionId = versionId,
            MinorVersion = 1,               // all new versions are drafts
            Content = content,
            BodyPlainText = bodyPlainText,
            PublishFrom = publishFrom,
            PublishTo = publishTo,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            CreatedBy = userId,
            UpdatedBy = userId
        });
        await context.SaveChangesAsync();
        await RecomputeCurrentAsync(nodeId, DateTime.UtcNow);
        return versionId;
    }

    public async Task UpdateVersionContentAsync(
        Guid nodeId, int versionId, string content, string bodyPlainText, string? userId)
    {
        var entity = await context.PageNodeVersions
            .FirstOrDefaultAsync(v => v.PageNodeId == nodeId && v.VersionId == versionId)
            ?? throw new InvalidOperationException(
                $"Version {versionId} for node {nodeId} not found.");

        entity.Content = content;
        entity.BodyPlainText = bodyPlainText;
        entity.MinorVersion += 1;           // each working-draft save bumps the minor counter
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task UpdateVersionWindowAsync(
        Guid nodeId, int versionId, DateTime? publishFrom, DateTime? publishTo, string? userId)
    {
        var entity = await context.PageNodeVersions
            .FirstOrDefaultAsync(v => v.PageNodeId == nodeId && v.VersionId == versionId)
            ?? throw new InvalidOperationException(
                $"Version {versionId} for node {nodeId} not found.");

        entity.PublishFrom = publishFrom;
        entity.PublishTo = publishTo;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        // Publishing or scheduling zeros the minor counter → version becomes an integer version.
        // Unpublishing (publishFrom == null) leaves minor unchanged — it becomes a past draft again.
        if (publishFrom is not null)
            entity.MinorVersion = 0;
        await context.SaveChangesAsync();
    }

    public Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId) =>
        context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId)
            .OrderByDescending(v => v.VersionId)
            .Select(VersionProjection)
            .ToListAsync();

    public async Task<PageNodeVersionDto?> GetLiveVersionAsync(Guid nodeId, DateTime nowUtc)
    {
        var windows = await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId)
            .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo))
            .ToListAsync();

        var liveId = LiveVersionResolver.Resolve(windows, nowUtc);
        if (liveId is null) return null;

        return await context.PageNodeVersions
            .AsNoTracking()
            .Where(v => v.PageNodeId == nodeId && v.VersionId == liveId)
            .Select(VersionProjection)
            .FirstOrDefaultAsync();
    }

    public async Task RecomputeCurrentAsync(Guid nodeId, DateTime nowUtc)
    {
        var windows = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo))
            .ToListAsync();

        var liveId = LiveVersionResolver.Resolve(windows, nowUtc);

        var all = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .ToListAsync();

        foreach (var v in all)
            v.IsCurrent = v.VersionId == liveId;

        await context.SaveChangesAsync();
    }

    public Task<bool> HasChildrenAsync(Guid nodeId) =>
        context.PageNodes.AnyAsync(n => n.ParentId == nodeId);

    public async Task SoftDeleteAsync(Guid nodeId, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");

        entity.DeletedDate = DateTime.UtcNow;
        entity.DeletedBy = userId;
        await context.SaveChangesAsync();
    }

    public Task<List<PageNodeDto>> GetDeletedAsync() =>
        context.PageNodes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(n => n.DeletedDate != null)
            .OrderByDescending(n => n.DeletedDate)
            .Select(NodeProjection)
            .ToListAsync();

    public async Task RestoreAsync(Guid nodeId, string? userId)
    {
        var entity = await context.PageNodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.Id == nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");

        entity.DeletedDate = null;
        entity.DeletedBy = null;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task SwapSortOrderAsync(Guid nodeId, Guid otherNodeId)
    {
        // FindAsync bypasses the global query filter — that is intentional here, since the
        // controller has already confirmed both nodes are live before calling this method.
        var nodeA = await context.PageNodes.FindAsync(nodeId)
            ?? throw new InvalidOperationException($"Page node {nodeId} not found.");
        var nodeB = await context.PageNodes.FindAsync(otherNodeId)
            ?? throw new InvalidOperationException($"Page node {otherNodeId} not found.");

        (nodeA.SortOrder, nodeB.SortOrder) = (nodeB.SortOrder, nodeA.SortOrder);
        await context.SaveChangesAsync();
    }

    public async Task SetSiblingOrderAsync(IReadOnlyList<(Guid Id, int SortOrder)> orders)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            foreach (var (id, sortOrder) in orders)
            {
                var node = await context.PageNodes.FindAsync(id)
                    ?? throw new InvalidOperationException($"Page node {id} not found.");
                node.SortOrder = sortOrder;
            }
            await context.SaveChangesAsync();
        });
    }

    public Task ExecuteInTransactionAsync(Func<Task> work) =>
        context.ExecuteInTransactionAsync(work);

    public async Task RenameNodeAndCascadeAsync(
        Guid id, string newSegment, string newPath, string newTitle, string? newSubtitle, string? newPageName, string? userId)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            var node = await context.PageNodes.FindAsync(id)
                ?? throw new InvalidOperationException($"Page node {id} not found.");

            var oldPath = node.Path;
            var now = DateTime.UtcNow;

            node.Segment = newSegment;
            node.Path = newPath;
            node.Title = newTitle;
            node.Subtitle = newSubtitle;
            node.PageName = newPageName;
            node.UpdatedDate = now;
            node.UpdatedBy = userId;

            // Only cascade when the path actually changed.
            if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                var oldPrefix = oldPath + "/";
                var descendants = await context.PageNodes
                    .Where(n => n.Id != id && n.Path.StartsWith(oldPrefix))
                    .ToListAsync();

                foreach (var d in descendants)
                {
                    d.Path = newPath + "/" + d.Path[oldPrefix.Length..];
                    d.UpdatedDate = now;
                    d.UpdatedBy = userId;
                }
            }

            await context.SaveChangesAsync();
        });
    }

    public async Task MoveNodeAsync(Guid id, Guid? newParentId, string newPath, int newSortOrder, string? userId)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            var node = await context.PageNodes.FindAsync(id)
                ?? throw new InvalidOperationException($"Page node {id} not found.");

            var oldPath = node.Path;
            var now = DateTime.UtcNow;

            node.ParentId = newParentId;
            node.Path = newPath;
            node.SortOrder = newSortOrder;
            node.UpdatedDate = now;
            node.UpdatedBy = userId;

            // Only cascade when the path actually changed. Descendants keep their tail; only the
            // prefix moves.
            if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
            {
                var oldPrefix = oldPath + "/";
                var descendants = await context.PageNodes
                    .Where(n => n.Id != id && n.Path.StartsWith(oldPrefix))
                    .ToListAsync();

                foreach (var d in descendants)
                {
                    d.Path = newPath + "/" + d.Path[oldPrefix.Length..];
                    d.UpdatedDate = now;
                    d.UpdatedBy = userId;
                }
            }

            await context.SaveChangesAsync();
        });
    }

    public async Task SetPageTypeAsync(Guid id, string pageType, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.PageType = pageType;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task SetShowInMenuAsync(Guid id, bool showInMenu, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.ShowInMenu = showInMenu;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task SetAppearInSearchAsync(Guid id, bool appearInSearch, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.AppearInSearch = appearInSearch;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task SetKeywordsAsync(Guid id, string? keywords, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.Keywords = string.IsNullOrWhiteSpace(keywords) ? null : keywords.Trim();
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task<PageNodeDto?> CopyNodeAsync(Guid sourceId, string newSegment, string newTitle, string? userId)
    {
        var source = await context.PageNodes
            .Where(n => n.Id == sourceId && n.DeletedDate == null)
            .Select(n => new
            {
                Node = n,
                Versions = context.PageNodeVersions
                    .Where(v => v.PageNodeId == sourceId)
                    .OrderBy(v => v.VersionId)
                    .ToList()
            })
            .FirstOrDefaultAsync();
        if (source is null) return null;

        var now = DateTime.UtcNow;
        var newId = Guid.NewGuid();
        var parentPath = source.Node.ParentId is null
            ? null
            : await context.PageNodes
                .Where(n => n.Id == source.Node.ParentId)
                .Select(n => (string?)n.Path)
                .FirstOrDefaultAsync();
        var newPath = parentPath is null ? newSegment : $"{parentPath}/{newSegment}";

        // Place the copy immediately after every existing sibling — no reshuffling of the
        // other rows required.
        var maxOrder = await context.PageNodes
            .Where(n => n.ParentId == source.Node.ParentId)
            .Select(n => (int?)n.SortOrder)
            .MaxAsync() ?? -1;

        var newNode = new PageNode
        {
            Id = newId,
            ParentId = source.Node.ParentId,
            Segment = newSegment,
            Path = newPath,
            SortOrder = maxOrder + 1,
            Title = newTitle,
            Subtitle = source.Node.Subtitle,
            PageName = source.Node.PageName,
            PageType = source.Node.PageType,
            ShowInMenu = source.Node.ShowInMenu,
            CreatedDate = now,
            UpdatedDate = now,
            CreatedBy = userId,
            UpdatedBy = userId,
        };
        context.PageNodes.Add(newNode);

        foreach (var v in source.Versions)
        {
            context.PageNodeVersions.Add(new PageNodeVersion
            {
                Id = Guid.NewGuid(),
                PageNodeId = newId,
                VersionId = v.VersionId,
                MinorVersion = v.MinorVersion,
                IsCurrent = v.IsCurrent,
                PublishFrom = v.PublishFrom,
                PublishTo = v.PublishTo,
                Content = v.Content,
                BodyPlainText = v.BodyPlainText,
                CreatedDate = now,
                CreatedBy = userId,
                UpdatedDate = now,
                UpdatedBy = userId,
            });
        }

        await context.SaveChangesAsync();
        return ToNodeDto(newNode);
    }

    // ── Staging (import) ────────────────────────────────────────────────────

    public async Task<PageNodeDto> CreateNodeForStagingAsync(
        Guid id, Guid? parentId, string segment, string path,
        string title, string? subtitle, string? pageName, string pageType, int sortOrder,
        bool appearInSearch, string? keywords, string? userId)
    {
        var now = DateTime.UtcNow;
        var entity = new PageNode
        {
            Id = id,
            ParentId = parentId,
            Segment = segment,
            Path = path,
            SortOrder = sortOrder,
            Title = title,
            Subtitle = subtitle,
            PageName = pageName,
            PageType = pageType,
            AppearInSearch = appearInSearch,
            Keywords = keywords,
            CreatedDate = now,
            UpdatedDate = now,
            CreatedBy = userId,
            UpdatedBy = userId
        };
        context.PageNodes.Add(entity);
        await context.SaveChangesAsync();
        return ToNodeDto(entity);
    }

    public async Task UpdateNodeForStagingAsync(
        Guid id, string segment, string path, string title, string? subtitle, string? pageName, int sortOrder,
        bool appearInSearch, string? keywords, string? userId)
    {
        var entity = await context.PageNodes.FindAsync(id)
            ?? throw new InvalidOperationException($"Page node {id} not found.");

        entity.Segment = segment;
        entity.Path = path;
        entity.Title = title;
        entity.Subtitle = subtitle;
        entity.PageName = pageName;
        entity.SortOrder = sortOrder;
        entity.AppearInSearch = appearInSearch;
        entity.Keywords = keywords;
        entity.UpdatedDate = DateTime.UtcNow;
        entity.UpdatedBy = userId;
        await context.SaveChangesAsync();
    }

    public async Task ReplaceAllVersionsForStagingAsync(
        Guid nodeId, IReadOnlyList<PageNodeVersionDto> versions, string? userId)
    {
        var now = DateTime.UtcNow;

        // Wipe existing versions in one round-trip; then add the bundle versions with their
        // original identities. IsCurrent is recomputed from windows below.
        var existing = await context.PageNodeVersions
            .Where(v => v.PageNodeId == nodeId)
            .ToListAsync();
        context.PageNodeVersions.RemoveRange(existing);

        foreach (var v in versions)
        {
            context.PageNodeVersions.Add(new PageNodeVersion
            {
                Id = Guid.NewGuid(),
                PageNodeId = nodeId,
                VersionId = v.VersionId,
                MinorVersion = v.MinorVersion,
                Content = v.Content,
                BodyPlainText = v.BodyPlainText,
                PublishFrom = v.PublishFrom,
                PublishTo = v.PublishTo,
                IsCurrent = false,      // RecomputeCurrentAsync sets this from windows
                CreatedDate = now,
                UpdatedDate = now,
                CreatedBy = userId,
                UpdatedBy = userId
            });
        }
        await context.SaveChangesAsync();
        await RecomputeCurrentAsync(nodeId, now);
    }

    // ── projections ──────────────────────────────────────────────────────────

    private static readonly System.Linq.Expressions.Expression<Func<PageNode, PageNodeDto>> NodeProjection =
        n => new PageNodeDto
        {
            Id = n.Id,
            ParentId = n.ParentId,
            Segment = n.Segment,
            Path = n.Path,
            SortOrder = n.SortOrder,
            Title = n.Title,
            Subtitle = n.Subtitle,
            PageName = n.PageName,
            PageType = n.PageType,
            ShowInMenu = n.ShowInMenu,
            AppearInSearch = n.AppearInSearch,
            Keywords = n.Keywords,
            DeletedDate = n.DeletedDate,
            DeletedBy = n.DeletedBy,
        };

    private static readonly System.Linq.Expressions.Expression<Func<PageNodeVersion, PageNodeVersionDto>> VersionProjection =
        v => new PageNodeVersionDto
        {
            Id = v.Id,
            VersionId = v.VersionId,
            MinorVersion = v.MinorVersion,
            IsCurrent = v.IsCurrent,
            PublishFrom = v.PublishFrom,
            PublishTo = v.PublishTo,
            Content = v.Content,
            BodyPlainText = v.BodyPlainText,
            CreatedDate = v.CreatedDate,
            CreatedBy = v.CreatedBy,
            UpdatedDate = v.UpdatedDate,
            UpdatedBy = v.UpdatedBy
        };

    // TRUNCATE ... CASCADE resets identity sequences and cascades through the FK from
    // PageNodeVersions and ContentBlockVersions. Postgres-specific; the app is Postgres-only.
    public Task TruncateAllContentAsync() =>
        context.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"", ""ContentBlocks"", ""ContentBlockVersions"" RESTART IDENTITY CASCADE;");

    private static PageNodeDto ToNodeDto(PageNode n) => new()
    {
        Id = n.Id,
        ParentId = n.ParentId,
        Segment = n.Segment,
        Path = n.Path,
        SortOrder = n.SortOrder,
        Title = n.Title,
        Subtitle = n.Subtitle,
        PageName = n.PageName,
        PageType = n.PageType,
        ShowInMenu = n.ShowInMenu,
        AppearInSearch = n.AppearInSearch,
        Keywords = n.Keywords
    };
}
