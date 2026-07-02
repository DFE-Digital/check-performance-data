using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Moves CMS content between environments. Identity and parentage are carried purely by stable
// GUIDs: a node/block is recognised across environments only by its Id, and a node's parent only
// by its ParentId. Segments/keys are content, never match keys; the materialised Path is rebuilt
// on import from the parent chain. Import replays the bundle through IPageNodeRepository's
// staging methods (never raw SQL): an unknown Id means "create new"; an unknown ParentId is an
// error (no orphan is created).
//
// v2 bundle shape: every page (folder, content, wiki) is a PageNode; folders never carry versions,
// content and wiki nodes carry their full version history so the target can replay it faithfully.
public sealed class ContentStagingService(
    IPageNodeRepository pageNodeRepository,
    IContentBlockRepository contentBlockRepository) : IContentStagingService
{
    private const int MaxDepth = 10;

    public async Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null)
    {
        var tree = await pageNodeRepository.GetTreeAsync();
        var byId = tree.ToDictionary(n => n.Id);

        var pageItems = new List<PageNodeBundleItem>();
        foreach (var node in OrderPreOrder(tree))
        {
            var versions = node.PageType == "folder"
                ? []
                : await pageNodeRepository.GetVersionsAsync(node.Id);

            pageItems.Add(new PageNodeBundleItem
            {
                Id = node.Id,
                ParentId = node.ParentId,
                Segment = node.Segment,
                Title = node.Title,
                PageType = node.PageType,
                SortOrder = node.SortOrder,
                Versions = versions
                    .OrderBy(v => v.VersionId)
                    .Select(v => new PageNodeVersionBundleItem
                    {
                        VersionId = v.VersionId,
                        MinorVersion = v.MinorVersion,
                        PublishFrom = v.PublishFrom,
                        PublishTo = v.PublishTo,
                        Content = v.Content,
                        BodyPlainText = v.BodyPlainText
                    })
                    .ToList()
            });
        }

        var blocks = await contentBlockRepository.GetAllAsync();
        var blockItems = blocks
            .Select(b => new ContentBlockBundleItem { Id = b.ContentId, Key = b.Key, BlockType = b.BlockType, Value = b.Value })
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        if (selection is not null)
        {
            var included = ExpandWithAncestors(selection.PageNodeIds, byId);
            pageItems = pageItems.Where(i => included.Contains(i.Id)).ToList();
            blockItems = blockItems.Where(i => selection.ContentBlockIds.Contains(i.Id)).ToList();
        }

        return new ContentBundle
        {
            Schema = ContentBundle.CurrentSchema,
            PageNodes = pageItems,
            ContentBlocks = blockItems
        };
    }

    public async Task<ContentCatalog> GetCatalogAsync()
    {
        var tree = await pageNodeRepository.GetTreeAsync();
        var byId = tree.ToDictionary(n => n.Id);

        var catalogPages = OrderPreOrder(tree).Select(n =>
        {
            var depth = DepthOf(n, byId);
            return new CatalogPage(n.Id, n.Title, n.Path, depth, n.CreatedDate, n.CreatedDate);
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

        var byId = BundleById(bundle.PageNodes);
        var resolvable = await ResolveableParentsAsync(bundle.PageNodes, byId);

        var pageItems = new List<PreviewItem>();
        foreach (var page in OrderForImport(bundle.PageNodes))
        {
            var path = VirtualPath(page, byId);
            if (!resolvable.Contains(page.Id))
            {
                pageItems.Add(new PreviewItem(page.Id, page.Title, path, false, null, ParentMissing: true));
                continue;
            }

            var existing = page.Id != Guid.Empty ? await pageNodeRepository.GetByIdAsync(page.Id) : null;
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

        var pages = OrderForImport(bundle.PageNodes);

        // Abort up front if any collision is left at the Fail default; a per-item Skip/Overwrite
        // resolves that item and keeps the import going.
        await GuardFailCollisionsAsync(pages, bundle.ContentBlocks, ModeFor);

        var result = new ContentImportResult();

        // Bundle node GUID -> materialised path, so a child resolves its parent's path without
        // re-querying the database on every iteration.
        var pathByContentId = new Dictionary<Guid, string>();

        foreach (var page in pages)
        {
            string? parentPath = null;
            if (page.ParentId is { } parentGuid)
            {
                parentPath = await ResolveParentPathAsync(parentGuid, pathByContentId);
                if (parentPath is null)
                {
                    // Unknown parent: never create an orphan under a blank/guessed parent.
                    result.Errors.Add($"Could not import ‘{page.Title}’ — its parent was not found in the bundle or this environment.");
                    continue;
                }
            }

            var path = parentPath is null ? page.Segment : $"{parentPath}/{page.Segment}";

            // Match purely by stable identity.
            var existing = page.Id != Guid.Empty ? await pageNodeRepository.GetByIdAsync(page.Id) : null;

            if (existing is not null)
            {
                if (ModeFor(page.Id) == ContentImportMode.Skip)
                {
                    result.PageNodesSkipped++;
                }
                else
                {
                    await pageNodeRepository.UpdateNodeForStagingAsync(
                        page.Id, page.Segment, path, page.Title, page.SortOrder, userId: null);
                    if (page.PageType != "folder")
                        await pageNodeRepository.ReplaceAllVersionsForStagingAsync(
                            page.Id, MapVersions(page.Versions), userId: null);
                    result.PageNodesUpdated++;
                }
                pathByContentId[page.Id] = existing.Path;
                continue;
            }

            // New identity. Create the node with its explicit Id, then replay versions.
            var created = await pageNodeRepository.CreateNodeForStagingAsync(
                page.Id, page.ParentId, page.Segment, path, page.Title, page.PageType, page.SortOrder, userId: null);
            if (page.PageType != "folder")
                await pageNodeRepository.ReplaceAllVersionsForStagingAsync(
                    created.Id, MapVersions(page.Versions), userId: null);
            result.PageNodesCreated++;
            pathByContentId[page.Id] = path;
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

    // ---- helpers ---------------------------------------------------------------------------

    // Refuse before making any change if a collision is left at the Fail mode (no per-item
    // Skip/Overwrite decision resolves it). Items with an explicit decision are not blocked.
    private async Task GuardFailCollisionsAsync(
        List<PageNodeBundleItem> pages,
        List<ContentBlockBundleItem> blocks,
        Func<Guid, ContentImportMode> modeFor)
    {
        foreach (var page in pages)
        {
            if (modeFor(page.Id) == ContentImportMode.Fail
                && page.Id != Guid.Empty
                && await pageNodeRepository.GetByIdAsync(page.Id) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — page ‘{page.Title}’ already exists in the target environment.");
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

    private async Task<string?> ResolveParentPathAsync(Guid parentGuid, Dictionary<Guid, string> pathByContentId)
    {
        if (pathByContentId.TryGetValue(parentGuid, out var cached)) return cached;
        var parent = await pageNodeRepository.GetByIdAsync(parentGuid);
        if (parent is null) return null;
        pathByContentId[parentGuid] = parent.Path;
        return parent.Path;
    }

    // The set of bundle node GUIDs whose parent chain bottoms out at a root or a parent that
    // exists in the target. Anything else has an unknown parent and cannot be imported.
    private async Task<HashSet<Guid>> ResolveableParentsAsync(
        IReadOnlyList<PageNodeBundleItem> pages, Dictionary<Guid, PageNodeBundleItem> byId)
    {
        // External parents (referenced but not in the bundle): resolvable only if present in target.
        var externalResolvable = new Dictionary<Guid, bool>();
        foreach (var parentGuid in pages
            .Where(p => p.ParentId is { } pid && !byId.ContainsKey(pid))
            .Select(p => p.ParentId!.Value)
            .Distinct())
        {
            externalResolvable[parentGuid] = await pageNodeRepository.GetByIdAsync(parentGuid) is not null;
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

    private static List<PageNodeVersionDto> MapVersions(IEnumerable<PageNodeVersionBundleItem> versions) =>
        versions
            .OrderBy(v => v.VersionId)
            .Select(v => new PageNodeVersionDto
            {
                Id = Guid.Empty,       // Ignored by ReplaceAllVersionsForStagingAsync (new GUID assigned)
                VersionId = v.VersionId,
                MinorVersion = v.MinorVersion,
                IsCurrent = false,     // Recomputed on write from windows
                PublishFrom = v.PublishFrom,
                PublishTo = v.PublishTo,
                Content = v.Content,
                BodyPlainText = v.BodyPlainText,
                CreatedDate = default,
                UpdatedDate = default
            })
            .ToList();

    // ---- ordering & paths -------------------------------------------------------------------

    private static Dictionary<Guid, PageNodeBundleItem> BundleById(IEnumerable<PageNodeBundleItem> pages) =>
        pages.Where(p => p.Id != Guid.Empty)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());

    // Parent-first (by depth through the GUID chain) so a child's parent is created first; within
    // a depth, ascending SortOrder so siblings keep their order.
    private static List<PageNodeBundleItem> OrderForImport(IReadOnlyList<PageNodeBundleItem> pages)
    {
        var byId = BundleById(pages);

        int DepthOfPage(PageNodeBundleItem page)
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
            .OrderBy(DepthOfPage)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    // The materialised path a bundle item would land at, rebuilt from parent segments.
    private static string VirtualPath(PageNodeBundleItem page, IReadOnlyDictionary<Guid, PageNodeBundleItem> byId)
    {
        var segments = new List<string>();
        PageNodeBundleItem? cursor = page;
        var depth = 0;
        while (cursor is not null && depth < MaxDepth)
        {
            segments.Insert(0, cursor.Segment);
            cursor = cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
            depth++;
        }
        return string.Join("/", segments);
    }

    // ---- export-side helpers ----------------------------------------------------------------

    private static List<PageNodeTreeItemDto> OrderPreOrder(List<PageNodeTreeItemDto> nodes)
    {
        static List<PageNodeTreeItemDto> Ordered(IEnumerable<PageNodeTreeItemDto> ns) =>
            ns.OrderBy(n => n.SortOrder).ThenBy(n => n.Title).ToList();

        var childrenByParent = nodes
            .Where(n => n.ParentId is not null)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => Ordered(g));

        var ordered = new List<PageNodeTreeItemDto>();
        var visited = new HashSet<Guid>();

        void Walk(List<PageNodeTreeItemDto> ns)
        {
            foreach (var n in ns)
            {
                if (!visited.Add(n.Id)) continue;
                ordered.Add(n);
                if (childrenByParent.TryGetValue(n.Id, out var kids)) Walk(kids);
            }
        }

        Walk(Ordered(nodes.Where(n => n.ParentId is null)));
        return ordered;
    }

    private static int DepthOf(PageNodeTreeItemDto node, Dictionary<Guid, PageNodeTreeItemDto> byId)
    {
        var depth = 0;
        var cursor = node;
        while (cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) && depth < MaxDepth)
        {
            depth++;
            cursor = parent;
        }
        return depth;
    }

    private static HashSet<Guid> ExpandWithAncestors(
        IReadOnlySet<Guid> selected, Dictionary<Guid, PageNodeTreeItemDto> byId)
    {
        var included = new HashSet<Guid>();

        foreach (var guid in selected)
        {
            if (!byId.TryGetValue(guid, out var cursor)) continue;
            var depth = 0;
            while (cursor is not null && depth < MaxDepth)
            {
                included.Add(cursor.Id);
                cursor = cursor.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;
                depth++;
            }
        }

        return included;
    }
}
