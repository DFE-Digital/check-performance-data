using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Moves CMS content between environments. Export reads the current wiki pages + content blocks
// and emits a slug-path-keyed bundle (database ids are environment-specific, so parentage is
// carried by path). Import replays the bundle through the normal application services — never
// raw SQL — resolving parents parent-first and handling existing content per ContentImportMode.
public sealed class ContentStagingService(
    IWikiService wikiService,
    IWikiRepository wikiRepository,
    IContentBlockService contentBlockService,
    IContentBlockRepository contentBlockRepository) : IContentStagingService
{
    private const int MaxDepth = 10;

    public async Task<ContentBundle> ExportAsync()
    {
        var pages = await wikiRepository.GetAllOrderedAsync();
        var byId = pages.ToDictionary(p => p.Id);

        string PathOf(WikiPageDto page)
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

        // Siblings ordered the way the wiki itself orders them (SortOrder, then Title); a
        // pre-order walk from the roots then emits the bundle in true tree order. Children are
        // keyed by non-nullable parent id (roots held separately) to avoid a null dictionary key.
        static List<WikiPageDto> Ordered(IEnumerable<WikiPageDto> nodes) =>
            nodes.OrderBy(p => p.SortOrder).ThenBy(p => p.Title).ToList();

        var roots = Ordered(pages.Where(p => p.ParentId is null));
        var childrenByParent = pages
            .Where(p => p.ParentId is not null)
            .GroupBy(p => p.ParentId!.Value)
            .ToDictionary(g => g.Key, g => Ordered(g));

        var wikiItems = new List<WikiPageBundleItem>();
        var visited = new HashSet<int>();

        void Walk(List<WikiPageDto> nodes)
        {
            foreach (var p in nodes)
            {
                if (!visited.Add(p.Id)) continue; // guard against any parent cycle
                var slugPath = PathOf(p);
                wikiItems.Add(new WikiPageBundleItem
                {
                    SlugPath = slugPath,
                    ParentSlugPath = ParentPath(slugPath),
                    Slug = p.Slug,
                    Title = p.Title,
                    Content = p.Content,
                    SortOrder = p.SortOrder
                });
                if (childrenByParent.TryGetValue(p.Id, out var kids)) Walk(kids);
            }
        }

        Walk(roots);

        var blocks = await contentBlockRepository.GetAllAsync();
        var blockItems = blocks
            .Select(b => new ContentBlockBundleItem { Key = b.Key, BlockType = b.BlockType, Value = b.Value })
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        return new ContentBundle
        {
            Schema = ContentBundle.CurrentSchema,
            WikiPages = wikiItems,
            ContentBlocks = blockItems
        };
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

        // Bundle slug-path -> target page id, so children resolve their parent without re-querying.
        var pathToId = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            var parentPath = ParentPath(page.SlugPath);
            int? parentId = null;

            if (parentPath.Length > 0)
            {
                parentId = await ResolveParentIdAsync(parentPath, pathToId);
                if (parentId is null)
                {
                    result.WikiPagesSkipped++;
                    result.Warnings.Add(
                        $"Skipped '{page.SlugPath}' — parent '{parentPath}' was not found in the bundle or target.");
                    continue;
                }
            }

            var leafSlug = LeafSlug(page.SlugPath);
            var existing = await wikiRepository.GetBySlugAndParentAsync(leafSlug, parentId);

            if (existing is not null)
            {
                if (mode == ContentImportMode.Skip)
                {
                    result.WikiPagesSkipped++;
                    pathToId[page.SlugPath] = existing.Id;
                    continue;
                }

                await wikiService.UpdatePageAsync(existing.Id,
                    new UpdateWikiPageDto { Title = page.Title, Content = page.Content, SortOrder = page.SortOrder });
                result.WikiPagesUpdated++;
                pathToId[page.SlugPath] = existing.Id;
            }
            else
            {
                var created = await wikiService.CreatePageAsync(
                    new CreateWikiPageDto { Title = page.Title, Content = page.Content, ParentId = parentId, SortOrder = page.SortOrder });
                result.WikiPagesCreated++;
                pathToId[page.SlugPath] = created.Id;
            }
        }

        foreach (var block in bundle.ContentBlocks)
        {
            var existing = await contentBlockService.GetByKeyAsync(block.Key);
            if (existing is not null && mode == ContentImportMode.Skip)
            {
                result.ContentBlocksSkipped++;
                continue;
            }

            await contentBlockService.SaveAsync(new SaveContentBlockDto
            {
                Key = block.Key,
                BlockType = block.BlockType,
                Value = block.Value
            });

            if (existing is null) result.ContentBlocksCreated++;
            else result.ContentBlocksUpdated++;
        }

        return result;
    }

    // Fail mode is all-or-nothing: refuse before making any change if the bundle would touch
    // content that already exists. A page can only pre-exist if its parent already exists in the
    // target, so a parent that resolves to null (present only in the bundle) means its child
    // cannot collide and is skipped here.
    private async Task GuardNoConflictsAsync(
        List<WikiPageBundleItem> pages, List<ContentBlockBundleItem> blocks)
    {
        foreach (var page in pages)
        {
            var parentPath = ParentPath(page.SlugPath);
            int? parentId = null;
            if (parentPath.Length > 0)
            {
                var parent = await wikiService.GetPageBySlugPathAsync(parentPath);
                if (parent is null) continue;
                parentId = parent.Id;
            }

            if (await wikiRepository.GetBySlugAndParentAsync(LeafSlug(page.SlugPath), parentId) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — wiki page '{page.SlugPath}' already exists in the target environment.");
        }

        foreach (var block in blocks)
        {
            if (await contentBlockService.GetByKeyAsync(block.Key) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — content block '{block.Key}' already exists in the target environment.");
        }
    }

    private async Task<int?> ResolveParentIdAsync(string parentPath, Dictionary<string, int> pathToId)
    {
        if (pathToId.TryGetValue(parentPath, out var id)) return id;
        var parent = await wikiService.GetPageBySlugPathAsync(parentPath);
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
