using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Moves CMS content between environments. Export reads the current wiki pages + content blocks
// and emits a bundle whose identity and parentage are carried by stable GUIDs (database ids are
// environment-specific; slugs/keys may be edited). Import replays the bundle through the normal
// application layer — never raw SQL — matching existing content by GUID so the same document is
// recognised across environments even after a rename, and handling it per ContentImportMode.
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
        var orderedPages = OrderPreOrder(pages);

        var wikiItems = orderedPages.Select(p =>
        {
            var slugPath = PathOf(p, byId);
            return new WikiPageBundleItem
            {
                Id = p.ContentId,
                ParentId = p.ParentId is { } pid && byId.TryGetValue(pid, out var parent)
                    ? parent.ContentId
                    : null,
                SlugPath = slugPath,
                ParentSlugPath = ParentPath(slugPath),
                Slug = p.Slug,
                Title = p.Title,
                Content = p.Content,
                SortOrder = p.SortOrder
            };
        }).ToList();

        var blocks = await contentBlockRepository.GetAllAsync();
        var blockItems = blocks
            .Select(b => new ContentBlockBundleItem { Id = b.ContentId, Key = b.Key, BlockType = b.BlockType, Value = b.Value })
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        if (selection is not null)
        {
            var included = ExpandWithAncestors(selection.WikiPageIds, orderedPages, byId);
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
            return new CatalogPage(p.ContentId, p.Title, slugPath, Depth(slugPath), p.CreatedAt, p.UpdatedAt);
        }).ToList();

        var blocks = await contentBlockRepository.GetAllAsync();
        var catalogBlocks = blocks
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .Select(b => new CatalogBlock(b.ContentId, b.Key, b.BlockType, b.LastSeenPath, b.CreatedAt, b.UpdatedAt))
            .ToList();

        return new ContentCatalog(catalogPages, catalogBlocks);
    }

    // Pre-order walk from the roots, siblings ordered the way the wiki orders them (SortOrder,
    // then Title), so a parent is always immediately followed by its subtree.
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
                if (!visited.Add(p.Id)) continue; // guard against any parent cycle
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

    // A selected page drags in its ancestor chain so the exported hierarchy is never orphaned.
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

    public async Task<ContentImportResult> ImportAsync(ContentBundle bundle, ContentImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        // Parent-first (by depth) so a child's parent is always present (in the target or earlier
        // in this pass); within a parent, ascending SortOrder so siblings are created in order.
        var pages = bundle.WikiPages
            .OrderBy(p => Depth(p.SlugPath))
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.SlugPath, StringComparer.Ordinal)
            .ToList();

        if (mode == ContentImportMode.Fail)
        {
            await GuardNoConflictsAsync(pages, bundle.ContentBlocks);
        }

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
                    result.WikiPagesSkipped++;
                    result.Warnings.Add(
                        $"Skipped '{page.SlugPath}' — its parent was not found in the bundle or target.");
                    continue;
                }
            }

            // Identity is the GUID; fall back to a slug+parent clash so a page created
            // independently in the target is recognised rather than colliding on the unique index.
            var existing = await MatchPageAsync(page, parentDbId);

            if (existing is not null)
            {
                if (mode == ContentImportMode.Skip)
                {
                    result.WikiPagesSkipped++;
                }
                else
                {
                    await wikiService.UpdatePageAsync(existing.Id,
                        new UpdateWikiPageDto { Title = page.Title, Content = page.Content, SortOrder = page.SortOrder });

                    // Matched on a slug clash rather than identity: adopt the bundle's identity so
                    // future syncs recognise this as the same page.
                    if (page.Id != Guid.Empty && existing.ContentId != page.Id)
                        await wikiRepository.SetContentIdAsync(existing.Id, page.Id);

                    result.WikiPagesUpdated++;
                }

                if (page.Id != Guid.Empty) contentIdToDbId[page.Id] = existing.Id;
            }
            else
            {
                var created = await wikiService.CreatePageAsync(new CreateWikiPageDto
                {
                    Title = page.Title,
                    Content = page.Content,
                    ParentId = parentDbId,
                    SortOrder = page.SortOrder,
                    ContentId = page.Id
                });
                result.WikiPagesCreated++;
                if (page.Id != Guid.Empty) contentIdToDbId[page.Id] = created.Id;
            }
        }

        foreach (var block in bundle.ContentBlocks)
        {
            var existing = await MatchBlockAsync(block);

            if (existing is not null)
            {
                if (mode == ContentImportMode.Skip)
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
            }
            else
            {
                await contentBlockRepository.ExecuteInTransactionAsync(async () =>
                {
                    var created = await contentBlockRepository.AddBlockAsync(
                        block.Key, block.BlockType, block.Value, block.Id);
                    await contentBlockRepository.AddVersionAsync(created.Id, block.Value, 1);
                });
                result.ContentBlocksCreated++;
            }
        }

        return result;
    }

    // Match a bundle page to an existing target page: by stable identity (GUID) first, then by a
    // slug+parent clash (a page independently created at the same slot under a different identity).
    private async Task<WikiPageDto?> MatchPageAsync(WikiPageBundleItem page, int? parentDbId) =>
        (page.Id != Guid.Empty ? await wikiRepository.GetByContentIdAsync(page.Id) : null)
        ?? await wikiRepository.GetBySlugAndParentAsync(LeafSlug(page.SlugPath), parentDbId);

    // Match a bundle block to an existing target block: by stable identity (GUID) first, then by
    // its Key (a block independently created with the same Key under a different identity).
    private async Task<ContentBlockDto?> MatchBlockAsync(ContentBlockBundleItem block) =>
        (block.Id != Guid.Empty ? await contentBlockRepository.GetByContentIdAsync(block.Id) : null)
        ?? await contentBlockRepository.GetByKeyAsync(block.Key);

    // Fail mode is all-or-nothing: refuse before making any change if the bundle would touch
    // content that already exists (by identity or by a slug/key clash). A parent present only in
    // the bundle resolves to null, so its child cannot clash and is skipped here.
    private async Task GuardNoConflictsAsync(
        List<WikiPageBundleItem> pages, List<ContentBlockBundleItem> blocks)
    {
        foreach (var page in pages)
        {
            int? parentDbId = null;
            if (page.ParentId is { } parentGuid)
            {
                var parent = await wikiRepository.GetByContentIdAsync(parentGuid);
                if (parent is null) continue;
                parentDbId = parent.Id;
            }

            if (await MatchPageAsync(page, parentDbId) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — wiki page '{page.SlugPath}' already exists in the target environment.");
        }

        foreach (var block in blocks)
        {
            if (await MatchBlockAsync(block) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — content block '{block.Key}' already exists in the target environment.");
        }
    }

    private async Task<int?> ResolveParentDbIdAsync(Guid parentGuid, Dictionary<Guid, int> contentIdToDbId)
    {
        if (contentIdToDbId.TryGetValue(parentGuid, out var id)) return id;
        var parent = await wikiRepository.GetByContentIdAsync(parentGuid);
        return parent?.Id;
    }

    private static int Depth(string slugPath) => slugPath.Count(c => c == '/');

    private static string ParentPath(string slugPath)
    {
        var idx = slugPath.LastIndexOf('/');
        return idx < 0 ? string.Empty : slugPath[..idx];
    }

    private static string LeafSlug(string slugPath)
    {
        var idx = slugPath.LastIndexOf('/');
        return idx < 0 ? slugPath : slugPath[(idx + 1)..];
    }
}
