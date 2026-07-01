using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Moves CMS content between environments. Identity and parentage are carried purely by stable
// GUIDs: a page/block is recognised across environments only by its Id, and a page's parent only
// by its ParentId. Slugs/keys are content, never match keys; the display slug path is rebuilt
// virtually by walking the ParentId chain. Import replays the bundle through the normal
// application layer (never raw SQL): an unknown Id means "create new"; an unknown ParentId is an
// error (no orphan is created).
public sealed class ContentStagingService(
    IWikiService wikiService,
    IWikiRepository wikiRepository,
    IContentBlockRepository contentBlockRepository) : IContentStagingService
{
    private const int MaxDepth = 10;

    public async Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null)
    {
        var pages = await wikiRepository.GetAllOrderedAsync();
        var byId = pages.ToDictionary(p => p.Id);

        var wikiItems = OrderPreOrder(pages).Select(p => new WikiPageBundleItem
        {
            Id = p.ContentId,
            ParentId = p.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent.ContentId : null,
            Title = p.Title,
            Slug = p.Slug,
            Content = p.Content,
            SortOrder = p.SortOrder
        }).ToList();

        var blocks = await contentBlockRepository.GetAllAsync();
        var blockItems = blocks
            .Select(b => new ContentBlockBundleItem { Id = b.ContentId, Key = b.Key, BlockType = b.BlockType, Value = b.Value })
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        if (selection is not null)
        {
            var included = ExpandWithAncestors(selection.WikiPageIds, pages, byId);
            wikiItems = wikiItems.Where(i => included.Contains(i.Id)).ToList();
            blockItems = blockItems.Where(i => selection.ContentBlockIds.Contains(i.Id)).ToList();
        }

        return new ContentBundle
        {
            Schema = ContentBundle.CurrentSchema,
            WikiPages = wikiItems,
            ContentBlocks = blockItems
        };
    }

    public async Task<ContentCatalog> GetCatalogAsync()
    {
        var pages = await wikiRepository.GetAllOrderedAsync();
        var byId = pages.ToDictionary(p => p.Id);

        var catalogPages = OrderPreOrder(pages).Select(p =>
        {
            var slugPath = PathOf(p, byId);
            return new CatalogPage(p.ContentId, p.Title, slugPath, slugPath.Count(c => c == '/'), p.CreatedAt, p.UpdatedAt);
        }).ToList();

        var blocks = await contentBlockRepository.GetAllAsync();
        var catalogBlocks = blocks
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .Select(b => new CatalogBlock(b.ContentId, b.Key, b.BlockType, b.LastSeenPath, b.CreatedAt, b.UpdatedAt))
            .ToList();

        return new ContentCatalog(catalogPages, catalogBlocks);
    }

    public async Task<ContentImportPreview> PreviewAsync(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var byId = BundleById(bundle.WikiPages);
        var resolvable = await ResolveableParentsAsync(bundle.WikiPages, byId);

        var pageItems = new List<PreviewItem>();
        foreach (var page in OrderForImport(bundle.WikiPages))
        {
            var path = VirtualSlugPath(page, byId);
            if (!resolvable.Contains(page.Id))
            {
                pageItems.Add(new PreviewItem(page.Id, page.Title, path, false, null, ParentMissing: true));
                continue;
            }

            var existing = page.Id != Guid.Empty ? await wikiRepository.GetByContentIdAsync(page.Id) : null;
            var description = existing is null
                ? null
                : existing.Title == page.Title
                    ? "Overwrites the existing page."
                    : $"Overwrites the existing page (currently titled ‘{existing.Title}’).";
            pageItems.Add(new PreviewItem(page.Id, page.Title, path, existing is not null, description));
        }

        var blockItems = new List<PreviewItem>();
        foreach (var block in bundle.ContentBlocks)
        {
            var existing = block.Id != Guid.Empty ? await contentBlockRepository.GetByContentIdAsync(block.Id) : null;
            var description = existing is null
                ? null
                : existing.Key == block.Key
                    ? "Overwrites the existing block."
                    : $"Overwrites the existing block (currently keyed ‘{existing.Key}’).";
            blockItems.Add(new PreviewItem(block.Id, block.Key, block.BlockType, existing is not null, description));
        }

        return new ContentImportPreview(pageItems, blockItems);
    }

    public async Task<ContentImportResult> ImportAsync(
        ContentBundle bundle,
        ContentImportMode mode,
        IReadOnlyDictionary<Guid, ContentImportMode>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        // Each item's effective mode is its per-collision decision, falling back to the global mode.
        ContentImportMode ModeFor(Guid id) =>
            decisions is not null && decisions.TryGetValue(id, out var m) ? m : mode;

        var pages = OrderForImport(bundle.WikiPages);

        // Abort up front if any collision is left at the Fail default; a per-item Skip/Overwrite
        // resolves that item and keeps the import going.
        await GuardFailCollisionsAsync(pages, bundle.ContentBlocks, ModeFor);

        var result = new ContentImportResult();

        // Bundle page GUID -> target db id, so a child resolves its parent without re-querying.
        var contentIdToDbId = new Dictionary<Guid, int>();

        foreach (var page in pages)
        {
            int? parentDbId = null;
            if (page.ParentId is { } parentGuid)
            {
                parentDbId = await ResolveParentDbIdAsync(parentGuid, contentIdToDbId);
                if (parentDbId is null)
                {
                    // Unknown parent: never create an orphan under a blank/guessed parent.
                    result.Errors.Add($"Could not import ‘{page.Title}’ — its parent was not found in the bundle or this environment.");
                    continue;
                }
            }

            // Match purely by stable identity.
            var existing = page.Id != Guid.Empty ? await wikiRepository.GetByContentIdAsync(page.Id) : null;

            if (existing is not null)
            {
                if (ModeFor(page.Id) == ContentImportMode.Skip)
                {
                    result.WikiPagesSkipped++;
                }
                else
                {
                    await wikiService.UpdatePageAsync(existing.Id,
                        new UpdateWikiPageDto { Title = page.Title, Content = page.Content, SortOrder = page.SortOrder, Slug = page.Slug });
                    result.WikiPagesUpdated++;
                }
                if (page.Id != Guid.Empty) contentIdToDbId[page.Id] = existing.Id;
                continue;
            }

            // New identity. Guard the unique (parent, slug) index so a same-slug sibling doesn't crash.
            var newSlug = string.IsNullOrWhiteSpace(page.Slug) ? WikiSlug.Generate(page.Title) : WikiSlug.Generate(page.Slug);
            if (await wikiRepository.SlugExistsAsync(newSlug, parentDbId))
            {
                result.Errors.Add($"Could not create ‘{page.Title}’ — a different page with the slug ‘{newSlug}’ already exists under its parent.");
                continue;
            }

            var created = await wikiService.CreatePageAsync(new CreateWikiPageDto
            {
                Title = page.Title,
                Content = page.Content,
                ParentId = parentDbId,
                SortOrder = page.SortOrder,
                ContentId = page.Id,
                Slug = page.Slug
            });
            result.WikiPagesCreated++;
            if (page.Id != Guid.Empty) contentIdToDbId[page.Id] = created.Id;
        }

        foreach (var block in bundle.ContentBlocks)
        {
            var existing = block.Id != Guid.Empty ? await contentBlockRepository.GetByContentIdAsync(block.Id) : null;

            if (existing is not null)
            {
                if (ModeFor(block.Id) == ContentImportMode.Skip)
                {
                    result.ContentBlocksSkipped++;
                    continue;
                }

                await contentBlockRepository.ExecuteInTransactionAsync(async () =>
                {
                    var maxVersion = await contentBlockRepository.GetMaxVersionNumberAsync(existing.Id);
                    await contentBlockRepository.UpdateForStagingAsync(
                        existing.Id, block.Key, block.BlockType, block.Value, block.Id);
                    await contentBlockRepository.AddVersionAsync(existing.Id, block.Value, maxVersion + 1);
                });
                result.ContentBlocksUpdated++;
                continue;
            }

            // New identity. Guard the unique Key so a same-keyed block doesn't crash the create.
            if (await contentBlockRepository.GetByKeyAsync(block.Key) is not null)
            {
                result.Errors.Add($"Could not create block ‘{block.Key}’ — a different block already uses that key.");
                continue;
            }

            await contentBlockRepository.ExecuteInTransactionAsync(async () =>
            {
                var created = await contentBlockRepository.AddBlockAsync(
                    block.Key, block.BlockType, block.Value, block.Id);
                await contentBlockRepository.AddVersionAsync(created.Id, block.Value, 1);
            });
            result.ContentBlocksCreated++;
        }

        return result;
    }

    // Refuse before making any change if a collision is left at the Fail mode (no per-item
    // Skip/Overwrite decision resolves it). Items with an explicit decision are not blocked.
    private async Task GuardFailCollisionsAsync(
        List<WikiPageBundleItem> pages,
        List<ContentBlockBundleItem> blocks,
        Func<Guid, ContentImportMode> modeFor)
    {
        foreach (var page in pages)
        {
            if (modeFor(page.Id) == ContentImportMode.Fail
                && page.Id != Guid.Empty
                && await wikiRepository.GetByContentIdAsync(page.Id) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — wiki page ‘{page.Title}’ already exists in the target environment.");
        }

        foreach (var block in blocks)
        {
            if (modeFor(block.Id) == ContentImportMode.Fail
                && block.Id != Guid.Empty
                && await contentBlockRepository.GetByContentIdAsync(block.Id) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — content block ‘{block.Key}’ already exists in the target environment.");
        }
    }

    private async Task<int?> ResolveParentDbIdAsync(Guid parentGuid, Dictionary<Guid, int> contentIdToDbId)
    {
        if (contentIdToDbId.TryGetValue(parentGuid, out var id)) return id;
        var parent = await wikiRepository.GetByContentIdAsync(parentGuid);
        return parent?.Id;
    }

    // The set of bundle page GUIDs whose parent chain bottoms out at a root or a parent that exists
    // in the target. Anything else has an unknown parent and cannot be imported.
    private async Task<HashSet<Guid>> ResolveableParentsAsync(
        IReadOnlyList<WikiPageBundleItem> pages, Dictionary<Guid, WikiPageBundleItem> byId)
    {
        // External parents (referenced but not in the bundle): resolvable only if present in target.
        var externalResolvable = new Dictionary<Guid, bool>();
        foreach (var parentGuid in pages
            .Where(p => p.ParentId is { } pid && !byId.ContainsKey(pid))
            .Select(p => p.ParentId!.Value)
            .Distinct())
        {
            externalResolvable[parentGuid] = await wikiRepository.GetByContentIdAsync(parentGuid) is not null;
        }

        var resolvable = new HashSet<Guid>();
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var p in pages)
            {
                if (resolvable.Contains(p.Id)) continue;
                var ok = p.ParentId is not { } parent
                    || (byId.ContainsKey(parent) ? resolvable.Contains(parent) : externalResolvable.GetValueOrDefault(parent));
                if (ok && resolvable.Add(p.Id)) changed = true;
            }
        }

        return resolvable;
    }

    // ---- ordering & paths -------------------------------------------------------------------

    private static Dictionary<Guid, WikiPageBundleItem> BundleById(IEnumerable<WikiPageBundleItem> pages) =>
        pages.Where(p => p.Id != Guid.Empty)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

    // Parent-first (by depth through the GUID chain) so a child's parent is created first; within a
    // depth, ascending SortOrder so siblings keep their order.
    private static List<WikiPageBundleItem> OrderForImport(IReadOnlyList<WikiPageBundleItem> pages)
    {
        var byId = BundleById(pages);

        int DepthOf(WikiPageBundleItem page)
        {
            var depth = 0;
            var cursor = page;
            while (cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) && depth < MaxDepth)
            {
                depth++;
                cursor = parent;
            }
            return depth;
        }

        return pages
            .OrderBy(DepthOf)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    // The display slug path, rebuilt by walking the ParentId chain and slugging each title.
    private static string VirtualSlugPath(WikiPageBundleItem page, IReadOnlyDictionary<Guid, WikiPageBundleItem> byId)
    {
        var segments = new List<string>();
        WikiPageBundleItem? cursor = page;
        var depth = 0;
        while (cursor is not null && depth < MaxDepth)
        {
            segments.Insert(0, string.IsNullOrWhiteSpace(cursor.Slug) ? WikiSlug.Generate(cursor.Title) : cursor.Slug);
            cursor = cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
            depth++;
        }
        return string.Join("/", segments);
    }

    // ---- export-side helpers (operate on live DB pages, real slugs) -------------------------

    private static List<WikiPageDto> OrderPreOrder(List<WikiPageDto> pages)
    {
        static List<WikiPageDto> Ordered(IEnumerable<WikiPageDto> nodes) =>
            nodes.OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToList();

        var childrenByParent = pages
            .Where(p => p.ParentId is not null)
            .GroupBy(p => p.ParentId!.Value)
            .ToDictionary(g => g.Key, g => Ordered(g));

        var ordered = new List<WikiPageDto>();
        var visited = new HashSet<int>();

        void Walk(List<WikiPageDto> nodes)
        {
            foreach (var p in nodes)
            {
                if (!visited.Add(p.Id)) continue;
                ordered.Add(p);
                if (childrenByParent.TryGetValue(p.Id, out var kids)) Walk(kids);
            }
        }

        Walk(Ordered(pages.Where(p => p.ParentId is null)));
        return ordered;
    }

    private static string PathOf(WikiPageDto page, Dictionary<int, WikiPageDto> byId)
    {
        var segments = new List<string>();
        int? cursor = page.Id;
        var depth = 0;
        while (cursor.HasValue && depth < MaxDepth && byId.TryGetValue(cursor.Value, out var node))
        {
            segments.Insert(0, node.Slug);
            cursor = node.ParentId;
            depth++;
        }
        return string.Join("/", segments);
    }

    private static HashSet<Guid> ExpandWithAncestors(
        IReadOnlySet<Guid> selected, List<WikiPageDto> pages, Dictionary<int, WikiPageDto> byId)
    {
        var byContentId = pages.ToDictionary(p => p.ContentId);
        var included = new HashSet<Guid>();

        foreach (var guid in selected)
        {
            var cursor = byContentId.GetValueOrDefault(guid);
            var depth = 0;
            while (cursor is not null && depth < MaxDepth)
            {
                included.Add(cursor.ContentId);
                cursor = cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
                depth++;
            }
        }

        return included;
    }
}
