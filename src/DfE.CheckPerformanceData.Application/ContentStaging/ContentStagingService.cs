using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    IContentBlockRepository contentBlockRepository,
    IHtmlRenderingService htmlRenderer,
    ContentBundleSanitiser? sanitiser = null,
    ILogger<ContentStagingService>? logger = null) : IContentStagingService
{
    // Ancestor walks used to be bounded by a fixed depth ceiling. The ceiling was only ever there
    // to stop a malformed bundle spinning forever, but it silently truncated legitimately deep
    // trees as well — and each truncation was its own defect: a saturated depth made every page
    // past the ceiling sort equal (so the import could attempt a child before its parent, record
    // it as "parent not found", and only create it on a later run), a truncated path made the
    // preview compare the wrong path for collisions, and a truncated ancestor set let a selective
    // export omit the top of a deep page's chain, producing a bundle that could never fully land.
    //
    // Walking until the chain repeats a node stops a cycle just as firmly, without putting a
    // ceiling on how deep a real page tree is allowed to be.
    private static IEnumerable<T> SelfAndAncestors<T>(T start, Func<T, Guid> idOf, Func<T, T?> parentOf)
        where T : class
    {
        var seen = new HashSet<Guid>();
        var cursor = start;
        while (cursor is not null && seen.Add(idOf(cursor)))
        {
            yield return cursor;
            cursor = parentOf(cursor);
        }
    }

    private readonly ILogger<ContentStagingService> _log =
        logger ?? NullLogger<ContentStagingService>.Instance;

    // Nullable + optional so the existing test set (which constructs the service with two
    // repositories) keeps compiling and the sanitiser branch is a no-op there. Real DI in
    // Program.cs wires the actual sanitiser in.
    private readonly ContentBundleSanitiser? _sanitiser = sanitiser;

    public async Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null)
    {
        var tree = await pageNodeRepository.GetTreeAsync();
        var byId = tree.ToDictionary(n => n.Id);

        // A null selection is "export everything", which still gets the default history cap —
        // the whole-environment export is the one most likely to be enormous.
        var maxVersionsPerNode = selection is null
            ? ContentExportSelection.DefaultMaxVersionsPerNode
            : selection.MaxVersionsPerNode;

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
                Subtitle = node.Subtitle,
                PageName = node.PageName,
                PageType = node.PageType,
                SortOrder = node.SortOrder,
                AppearInSearch = node.AppearInSearch,
                Keywords = node.Keywords,
                Versions = TrimHistory(versions, maxVersionsPerNode)
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
            .Select(b => new ContentBlockBundleItem { Id = b.ContentId, Key = b.Key, BlockType = b.BlockType, Value = b.Value, AppearInSearch = b.AppearInSearch, Keywords = b.Keywords })
            .OrderBy(b => b.Key, StringComparer.Ordinal)
            .ToList();

        // Null id sets mean "no filter" — an export can be whole-environment and still carry a
        // history preference, so the two are decided independently.
        if (selection?.PageNodeIds is { } selectedPages)
        {
            var included = ExpandWithAncestors(selectedPages, byId);
            pageItems = pageItems.Where(i => included.Contains(i.Id)).ToList();
        }

        if (selection?.ContentBlockIds is { } selectedBlocks)
        {
            blockItems = blockItems.Where(i => selectedBlocks.Contains(i.Id)).ToList();
        }

        return new ContentBundle
        {
            Schema = ContentBundle.CurrentSchema,
            PageNodes = pageItems,
            ContentBlocks = blockItems
        };
    }

    // Keeps the most recent `max` versions of a node, in ascending version order, plus every
    // version the target environment would still need to behave like the source.
    //
    // Two kinds of version are kept regardless of recency, because losing either changes what
    // the environment does rather than merely how much history it remembers:
    //
    //  * The one that is live NOW. Liveness is resolved from the publish windows on every
    //    request, so it is not necessarily the highest-numbered version — a page carrying a
    //    stack of unpublished drafts serves something underneath them. Resolved here with the
    //    same LiveVersionResolver the renderer uses, deliberately NOT with the IsCurrent
    //    column: that column is a cache written only when something writes to the page, and
    //    nothing recomputes it as time passes, so a temporary notice that expired months ago
    //    still carries the flag while the site has long since fallen back to an older version.
    //    Trusting it would drop the version actually being served and, where the stale one's
    //    window has closed, leave the imported page with no live version at all.
    //
    //  * Anything scheduled to go live later. A future-dated version is not current yet and is
    //    not recent once drafts pile on top of it, so both other rules miss it — and the
    //    scheduled publish would then simply never happen in the target.
    //
    // Both are rare, so in the ordinary case this returns exactly `max` versions.
    private static List<PageNodeVersionDto> TrimHistory(List<PageNodeVersionDto> versions, int? max)
    {
        if (max is not int cap || versions.Count <= cap)
        {
            return versions.OrderBy(v => v.VersionId).ToList();
        }

        var nowUtc = DateTime.UtcNow;
        var kept = versions.OrderByDescending(v => v.VersionId).Take(cap).ToList();
        var keptIds = kept.Select(v => v.VersionId).ToHashSet();

        var liveId = LiveVersionResolver.Resolve(
            versions.Select(v => new PageVersionWindow(v.VersionId, v.PublishFrom, v.PublishTo)),
            nowUtc);
        if (liveId is int id && keptIds.Add(id))
        {
            kept.Add(versions.First(v => v.VersionId == id));
        }

        foreach (var scheduled in versions.Where(v => v.PublishFrom is { } from && from > nowUtc))
        {
            if (keptIds.Add(scheduled.VersionId))
            {
                kept.Add(scheduled);
            }
        }

        return kept.OrderBy(v => v.VersionId).ToList();
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

        // Validate up front. Fatal issues in the shape or contents of the bundle mean the
        // preview can't be produced safely — the operator should get the error banner from
        // the landing page instead of a broken half-preview.
        var issues = ContentBundleValidator.Validate(bundle);
        if (issues.Any(i => i.Severity == ValidationSeverity.Fatal))
        {
            throw new ContentImportValidationException(issues);
        }

        // Sanitise HTML in place before the preview so any bundle-json we round-trip through
        // the hidden form field is already clean. Idempotent — running it again at Import
        // time (below) is a no-op on already-scrubbed content.
        _sanitiser?.SanitiseInPlace(bundle);

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
        IReadOnlyDictionary<Guid, ContentImportMode>? decisions = null,
        ContentImportMode newItemMode = ContentImportMode.Replace)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        // Re-validate at Import time. The bundle round-trips through a hidden form field
        // between Preview and Import — belt-and-braces so a tampered field can't smuggle a
        // fatal issue past the earlier check.
        var issues = ContentBundleValidator.Validate(bundle);
        if (issues.Any(i => i.Severity == ValidationSeverity.Fatal))
        {
            throw new ContentImportValidationException(issues);
        }

        // Sanitise HTML in place. Idempotent — if PreviewAsync already sanitised, this is
        // free. Any items that were still dirty (rare — implies the operator hand-edited
        // the round-tripped JSON) surface as a warning on the result banner.
        var sanitised = _sanitiser?.SanitiseInPlace(bundle) ?? 0;

        _log.LogInformation(
            "ContentStaging import starting: {PageCount} page(s), {BlockCount} block(s), collisionMode={Mode}, newItemMode={NewMode}, decisions={DecisionCount}",
            bundle.PageNodes.Count, bundle.ContentBlocks.Count, mode, newItemMode, decisions?.Count ?? 0);

        // Per-item explicit decision wins; the caller distinguishes "existing item" from "new
        // item" via two separate fallback modes. isExisting is passed by the loops so we can
        // pick the right default when there is no explicit decision.
        ContentImportMode ModeFor(Guid id, bool isExisting) =>
            decisions is not null && decisions.TryGetValue(id, out var m)
                ? m
                : (isExisting ? mode : newItemMode);

        var pages = OrderForImport(bundle.PageNodes);

        // Abort up front if any collision is left at the Fail default; a per-item Skip/Overwrite
        // resolves that item and keeps the import going.
        await GuardFailCollisionsAsync(pages, bundle.ContentBlocks, ModeFor);

        var result = new ContentImportResult();

        // Surface the sanitiser's effect prominently — if the operator uploaded a bundle
        // with unsafe HTML we want them to know that N items were scrubbed on the way in,
        // not silently accept the cleaned payload as if it had always been clean.
        if (sanitised > 0)
        {
            result.Warnings.Add(
                $"Sanitised HTML in {sanitised} bundle item(s) on import (script tags, event handlers, or javascript: URLs were removed).");
        }

        // Bundle node GUID -> materialised path, so a child resolves its parent's path without
        // re-querying the database on every iteration.
        var pathByContentId = new Dictionary<Guid, string>();

        // Bundle node GUID -> the local node GUID that it actually resolves to. Populated when
        // path-fallback matches a bundle page to an existing local node with a different GUID
        // (default-root seeder, manually-authored roots, etc.). A child in the bundle carries
        // its bundle-parent's GUID as ParentId, but the FK constraint checks against the local
        // row — so we rewrite ParentId through this map at create time.
        var localIdByBundleId = new Dictionary<Guid, Guid>();

        foreach (var page in pages)
        {
            try
            {
                string? parentPath = null;
                if (page.ParentId is { } parentGuid)
                {
                    parentPath = await ResolveParentPathAsync(parentGuid, pathByContentId);
                    if (parentPath is null)
                    {
                        _log.LogWarning(
                            "ContentStaging: skipping page {PageId} ‘{Title}’ — parent {ParentId} not found",
                            page.Id, page.Title, parentGuid);
                        // Unknown parent: never create an orphan under a blank/guessed parent.
                        result.Errors.Add($"Could not import ‘{page.Title}’ — its parent was not found in the bundle or this environment.");
                        continue;
                    }
                }

                var path = parentPath is null ? page.Segment : $"{parentPath}/{page.Segment}";

                // Primary match: stable GUID identity — the ideal path when the target env has
                // seen this content before.
                var existing = page.Id != Guid.Empty ? await pageNodeRepository.GetByIdAsync(page.Id) : null;

                // Fallback match: the target env already has a node at this path but with a
                // different GUID. Happens routinely with the default-root seeder (Support, Help,
                // Wiki, Guidance are created on startup) and with any manually-authored page
                // that shares a segment with the bundle. Path is the natural human identity, so
                // treat it as a collision even though the GUIDs don't line up.
                var pathMatch = existing is null
                    ? await pageNodeRepository.GetByPathAsync(path)
                    : null;
                if (pathMatch is not null)
                {
                    _log.LogInformation(
                        "ContentStaging: page ‘{Title}’ at {Path} — GUID mismatch (bundle={BundleId}, local={LocalId}); treating as existing via path match",
                        page.Title, path, page.Id, pathMatch.Id);
                    existing = pathMatch;
                }

                if (existing is not null)
                {
                    // If the match came from Path (not GUID), use the LOCAL Id everywhere so the
                    // update targets the actual row and children get the right pointer.
                    var effectiveId = existing.Id;
                    if (ModeFor(page.Id, isExisting: true) == ContentImportMode.Skip)
                    {
                        _log.LogDebug("ContentStaging: page {PageId} ‘{Title}’ exists, mode=Skip", page.Id, page.Title);
                        result.PageNodesSkipped++;
                    }
                    else
                    {
                        await pageNodeRepository.UpdateNodeForStagingAsync(
                            effectiveId, page.Segment, path, page.Title, page.Subtitle, page.PageName, page.SortOrder,
                            page.AppearInSearch, page.Keywords, userId: null);
                        if (page.PageType != "folder")
                            await pageNodeRepository.ReplaceAllVersionsForStagingAsync(
                                effectiveId, MapVersions(page.Versions), userId: null);
                        _log.LogDebug("ContentStaging: page {PageId} ‘{Title}’ updated ({VersionCount} versions)", page.Id, page.Title, page.Versions.Count);
                        result.PageNodesUpdated++;
                    }
                    // Children in the bundle reference *this bundle page's* Id, so map both the
                    // bundle Id and the local Id to the existing path — children can then be
                    // adopted under whichever pointer they carry.
                    pathByContentId[page.Id] = existing.Path;
                    if (effectiveId != page.Id)
                    {
                        pathByContentId[effectiveId] = existing.Path;
                        localIdByBundleId[page.Id] = effectiveId;
                    }
                    continue;
                }

                // New identity. Skip decision on a new item honours the "pick a subset" flow
                // from the preview page — the operator can uncheck items they don't want to
                // land in this environment. Descendants of a skipped page will hit the
                // "parent not found" branch and be flagged in the errors list, which is what
                // the operator expects when they opt out of a folder.
                if (ModeFor(page.Id, isExisting: false) == ContentImportMode.Skip)
                {
                    _log.LogDebug("ContentStaging: page {PageId} ‘{Title}’ is new, mode=Skip — leaving out of import", page.Id, page.Title);
                    result.PageNodesSkipped++;
                    continue;
                }

                // Rewrite ParentId through the localId map so a bundle parent whose local match
                // has a different GUID still lines up against the FK.
                var effectiveParentId = page.ParentId;
                if (page.ParentId is { } bundleParent && localIdByBundleId.TryGetValue(bundleParent, out var localParent))
                    effectiveParentId = localParent;

                var created = await pageNodeRepository.CreateNodeForStagingAsync(
                    page.Id, effectiveParentId, page.Segment, path, page.Title, page.Subtitle, page.PageName, page.PageType, page.SortOrder,
                    page.AppearInSearch, page.Keywords, userId: null);
                if (page.PageType != "folder")
                    await pageNodeRepository.ReplaceAllVersionsForStagingAsync(
                        created.Id, MapVersions(page.Versions), userId: null);
                _log.LogDebug("ContentStaging: page {PageId} ‘{Title}’ created ({VersionCount} versions)", page.Id, page.Title, page.Versions.Count);
                result.PageNodesCreated++;
                pathByContentId[page.Id] = path;
            }
            catch (Exception ex)
            {
                // Never abort the whole import on a single-page failure — record it and continue,
                // so the operator can see how far it got and what specifically broke.
                _log.LogError(ex,
                    "ContentStaging: page {PageId} ‘{Title}’ ({Segment}, pageType={PageType}) failed to import",
                    page.Id, page.Title, page.Segment, page.PageType);
                result.Errors.Add($"Failed to import ‘{page.Title}’: {ex.GetType().Name} — {ex.Message}");
            }
        }

        foreach (var block in bundle.ContentBlocks)
        {
            try
            {
                var existing = block.Id != Guid.Empty ? await contentBlockRepository.GetByContentIdAsync(block.Id) : null;

                // Fallback: same key, different GUID. Happens when the target env has
                // auto-provisioned the block on its own (EditableContent view components seed
                // blocks the first time a page renders, with fresh GUIDs). Key is the human
                // identity — treat it as a collision even without a GUID match.
                if (existing is null)
                {
                    var keyMatch = await contentBlockRepository.GetByKeyAsync(block.Key);
                    if (keyMatch is not null)
                    {
                        _log.LogInformation(
                            "ContentStaging: block ‘{Key}’ — GUID mismatch (bundle={BundleId}, local={LocalId}); treating as existing via key match",
                            block.Key, block.Id, keyMatch.Id);
                        existing = keyMatch;
                    }
                }

                if (existing is not null)
                {
                    if (ModeFor(block.Id, isExisting: true) == ContentImportMode.Skip)
                    {
                        result.ContentBlocksSkipped++;
                        continue;
                    }

                    await contentBlockRepository.ExecuteInTransactionAsync(async () =>
                    {
                        var maxVersion = await contentBlockRepository.GetMaxVersionNumberAsync(existing.Id);
                        await contentBlockRepository.UpdateForStagingAsync(
                            existing.Id, block.Key, block.BlockType, block.Value, PlainTextOf(block.Value),
                            block.Id, block.AppearInSearch, block.Keywords);
                        await contentBlockRepository.AddVersionAsync(existing.Id, block.Value, maxVersion + 1);
                    });
                    result.ContentBlocksUpdated++;
                    continue;
                }

                // New block. Honour a Skip decision so the operator can leave items out of
                // the import from the preview page.
                if (ModeFor(block.Id, isExisting: false) == ContentImportMode.Skip)
                {
                    _log.LogDebug("ContentStaging: block ‘{Key}’ is new, mode=Skip — leaving out of import", block.Key);
                    result.ContentBlocksSkipped++;
                    continue;
                }

                await contentBlockRepository.ExecuteInTransactionAsync(async () =>
                {
                    var created = await contentBlockRepository.AddBlockAsync(
                        block.Key, block.BlockType, block.Value, PlainTextOf(block.Value),
                        block.Id, block.AppearInSearch, block.Keywords);
                    await contentBlockRepository.AddVersionAsync(created.Id, block.Value, 1);
                });
                result.ContentBlocksCreated++;
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "ContentStaging: block {BlockId} ‘{Key}’ ({BlockType}) failed to import",
                    block.Id, block.Key, block.BlockType);
                result.Errors.Add($"Failed to import block ‘{block.Key}’: {ex.GetType().Name} — {ex.Message}");
            }
        }

        _log.LogInformation(
            "ContentStaging import finished: pages created={PagesCreated} updated={PagesUpdated} skipped={PagesSkipped}; blocks created={BlocksCreated} updated={BlocksUpdated} skipped={BlocksSkipped}; errors={ErrorCount} warnings={WarningCount}",
            result.PageNodesCreated, result.PageNodesUpdated, result.PageNodesSkipped,
            result.ContentBlocksCreated, result.ContentBlocksUpdated, result.ContentBlocksSkipped,
            result.Errors.Count, result.Warnings.Count);

        return result;
    }

    // ---- helpers ---------------------------------------------------------------------------

    // Refuse before making any change if a collision is left at the Fail mode (no per-item
    // Skip/Overwrite decision resolves it). Items with an explicit decision are not blocked.
    // Only invoked with isExisting: true — Fail only makes sense for collisions.
    private async Task GuardFailCollisionsAsync(
        List<PageNodeBundleItem> pages,
        List<ContentBlockBundleItem> blocks,
        Func<Guid, bool, ContentImportMode> modeFor)
    {
        foreach (var page in pages)
        {
            if (modeFor(page.Id, true) == ContentImportMode.Fail
                && page.Id != Guid.Empty
                && await pageNodeRepository.GetByIdAsync(page.Id) is not null)
                throw new ContentImportConflictException(
                    $"Import aborted — page ‘{page.Title}’ already exists in the target environment.");
        }

        foreach (var block in blocks)
        {
            if (modeFor(block.Id, true) == ContentImportMode.Fail
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

    // Mirrors ContentBlockService.PlainTextOf — render → strip so the ValuePlainText column
    // gets the same clean plaintext the on-page save path writes. Keeps the search vector's
    // tokens matching between "editor saved this block" and "staging imported this block".
    private string PlainTextOf(string? html) =>
        htmlRenderer.StripTagsToPlainText(htmlRenderer.RenderHtml(html));

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

        int DepthOfPage(PageNodeBundleItem page) =>
            SelfAndAncestors(page, p => p.Id, p => BundleParent(p, byId)).Count() - 1;

        return pages
            .OrderBy(DepthOfPage)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Title, StringComparer.Ordinal)
            .ToList();
    }

    // The materialised path a bundle item would land at, rebuilt from parent segments.
    private static string VirtualPath(PageNodeBundleItem page, IReadOnlyDictionary<Guid, PageNodeBundleItem> byId)
    {
        var segments = SelfAndAncestors(page, p => p.Id, p => BundleParent(p, byId))
            .Select(p => p.Segment)
            .Reverse();
        return string.Join("/", segments);
    }

    private static PageNodeBundleItem? BundleParent(
        PageNodeBundleItem page, IReadOnlyDictionary<Guid, PageNodeBundleItem> byId) =>
        page.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;

    private static PageNodeTreeItemDto? TreeParent(
        PageNodeTreeItemDto node, IReadOnlyDictionary<Guid, PageNodeTreeItemDto> byId) =>
        node.ParentId is { } pid && byId.TryGetValue(pid, out var parent) ? parent : null;

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

    private static int DepthOf(PageNodeTreeItemDto node, Dictionary<Guid, PageNodeTreeItemDto> byId) =>
        SelfAndAncestors(node, n => n.Id, n => TreeParent(n, byId)).Count() - 1;

    private static HashSet<Guid> ExpandWithAncestors(
        IReadOnlySet<Guid> selected, Dictionary<Guid, PageNodeTreeItemDto> byId)
    {
        var included = new HashSet<Guid>();

        foreach (var guid in selected)
        {
            if (!byId.TryGetValue(guid, out var cursor)) continue;
            foreach (var node in SelfAndAncestors(cursor, n => n.Id, n => TreeParent(n, byId)))
                included.Add(node.Id);
        }

        return included;
    }
}
