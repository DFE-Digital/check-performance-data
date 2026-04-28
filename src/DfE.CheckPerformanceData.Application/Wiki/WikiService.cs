using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed partial class WikiService(
	IWikiRepository repository,
	IHtmlRenderingService htmlRenderer) : IWikiService
{
	private const int MaxDepth = 10;
	private const int MinQueryLength = 2;
	private const int MaxQueryLength = 200;
	private const int DefaultPageSize = 20;

	public async Task<List<WikiPageTreeNodeDto>> GetNavigationTreeAsync()
	{
		var pages = await repository.GetAllOrderedAsync();

		var lookup = pages
			.Select(p => new { p.Id, p.Title, p.Slug, p.ParentId, p.SortOrder })
			.ToLookup(p => p.ParentId);

		List<WikiPageTreeNodeDto> BuildChildren(int? parentId, string parentSlugPath, int depth)
		{
			if (depth >= MaxDepth) return [];

			return lookup[parentId]
				.Select(p =>
				{
					var slugPath = string.IsNullOrEmpty(parentSlugPath)
						? p.Slug
						: $"{parentSlugPath}/{p.Slug}";

					return new WikiPageTreeNodeDto
					{
						Id = p.Id,
						Title = p.Title,
						Slug = p.Slug,
						SlugPath = slugPath,
						ParentId = p.ParentId,
						SortOrder = p.SortOrder,
						Children = BuildChildren(p.Id, slugPath, depth + 1)
					};
				})
				.ToList();
		}

		return BuildChildren(null, string.Empty, 0);
	}

	public async Task<WikiPageDto?> GetPageBySlugPathAsync(string slugPath)
	{
		var slugs = slugPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (slugs.Length == 0) return null;

		int? parentId = null;
		WikiPageDto? page = null;

		foreach (var slug in slugs)
		{
			page = await repository.GetBySlugAndParentAsync(slug, parentId);
			if (page == null) return null;
			parentId = page.Id;
		}

		return page == null ? null : EnrichDto(page, slugPath);
	}

	public async Task<WikiPageDto?> GetPageByIdAsync(int id)
	{
		var page = await repository.GetByIdAsync(id);
		if (page == null) return null;

		var slugPath = await BuildSlugPathAsync(page);
		return EnrichDto(page, slugPath);
	}

	public async Task<WikiSearchResultsDto> SearchAsync(string query, int page, int pageSize = 20)
	{
		var raw = query ?? string.Empty;
		var trimmed = raw.Trim();

		if (string.IsNullOrEmpty(trimmed))
		{
			return new WikiSearchResultsDto
			{
				Query = string.Empty,
				Page = 1,
				PageSize = pageSize,
				TotalCount = 0,
				Items = [],
				InvalidReason = SearchInvalidReason.EmptyQuery
			};
		}

		if (trimmed.Length < MinQueryLength)
		{
			return new WikiSearchResultsDto
			{
				Query = trimmed,
				Page = 1,
				PageSize = pageSize,
				TotalCount = 0,
				Items = [],
				InvalidReason = SearchInvalidReason.BelowMinimumLength
			};
		}

		// cap length BEFORE reaching Postgres.
		if (trimmed.Length > MaxQueryLength)
		{
			trimmed = trimmed[..MaxQueryLength];
		}

		var safePageSize = pageSize <= 0 ? DefaultPageSize : pageSize;
		var initialPage = page < 1 ? 1 : page;
		var initialSkip = (initialPage - 1) * safePageSize;

		var (items, total) = await repository.SearchAsync(trimmed, initialSkip, safePageSize);

		// Out-of-range page? Clamp + requery. Only when total > 0 AND initialPage exceeds TotalPages.
		var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)safePageSize);
		var finalPage = initialPage;
		if (total > 0 && initialPage > totalPages)
		{
			finalPage = totalPages;
			var clampedSkip = (finalPage - 1) * safePageSize;
			(items, total) = await repository.SearchAsync(trimmed, clampedSkip, safePageSize);
		}

		// Per-result slug-path enrichment — one GetAllOrderedAsync walk.
		if (items.Count > 0)
		{
			var allPages = await repository.GetAllOrderedAsync();
			var lookup = allPages.ToDictionary(p => p.Id, p => (p.Slug, p.ParentId));
			foreach (var item in items)
			{
				item.SlugPath = ResolveSlugPath(item.Id, lookup);
			}
		}

		return new WikiSearchResultsDto
		{
			Query = trimmed,
			Page = finalPage,
			PageSize = safePageSize,
			TotalCount = total,
			Items = items,
			InvalidReason = null
		};
	}

	private static string ResolveSlugPath(int id, Dictionary<int, (string Slug, int? ParentId)> lookup)
	{
		var segments = new List<string>();
		var cursor = (int?)id;
		var depth = 0;
		while (cursor.HasValue && depth < MaxDepth && lookup.TryGetValue(cursor.Value, out var node))
		{
			segments.Insert(0, node.Slug);
			cursor = node.ParentId;
			depth++;
		}
		return string.Join("/", segments);
	}

	public async Task<WikiPageDto> CreatePageAsync(CreateWikiPageDto dto)
	{
		var slug = GenerateSlug(dto.Title);

		if (await repository.SlugExistsAsync(slug, dto.ParentId))
			slug = $"{slug}-{DateTime.UtcNow.Ticks}";

		var maxSortOrder = await repository.GetMaxSortOrderAsync(dto.ParentId);

		// XSS: sanitise → strip tags → feed Postgres's STORED SearchVector.
		var sanitisedHtml = htmlRenderer.RenderHtml(dto.Content) ?? string.Empty;
		var bodyPlainText = htmlRenderer.StripTagsToPlainText(sanitisedHtml);

		WikiPageDto? page = null;
		await repository.ExecuteInTransactionAsync(async () =>
		{
			page = await repository.AddPageAsync(dto, slug, maxSortOrder + 1, bodyPlainText);
			await repository.AddVersionAsync(page.Id, dto.Title, dto.Content, 1);
		});

		var slugPath = await BuildSlugPathAsync(page!);
		return EnrichDto(page!, slugPath);
	}

	public async Task<WikiPageDto> UpdatePageAsync(int id, UpdateWikiPageDto dto)
	{
		var slug = GenerateSlug(dto.Title);

		// XSS: sanitise → strip tags → feed Postgres's STORED SearchVector.
		var sanitisedHtml = htmlRenderer.RenderHtml(dto.Content) ?? string.Empty;
		var bodyPlainText = htmlRenderer.StripTagsToPlainText(sanitisedHtml);

		//await repository.UpdatePageAsync(id, dto.Title, dto.Content, slug, bodyPlainText);

		var maxVersion = await repository.GetMaxVersionNumberAsync(id);
		await repository.AddVersionAsync(id, dto.Title, dto.Content, maxVersion + 1);
        await repository.ExecuteInTransactionAsync(async () =>
        {
            var maxVersion = await repository.GetMaxVersionNumberAsync(id);

            if (maxVersion == 0)
            {
                var existing = await repository.GetByIdAsync(id)
                    ?? throw new InvalidOperationException($"Wiki page {id} not found.");
                await repository.AddVersionAsync(id, existing.Title, existing.Content, 1);
                await repository.UpdatePageAsync(id, dto.Title, dto.Content, slug);
                await repository.AddVersionAsync(id, dto.Title, dto.Content, 2);
            }
            else
            {
                await repository.UpdatePageAsync(id, dto.Title, dto.Content, slug);
                await repository.AddVersionAsync(id, dto.Title, dto.Content, maxVersion + 1);
            }
        });

		var page = await repository.GetByIdAsync(id)
			?? throw new InvalidOperationException($"Wiki page {id} not found.");

		var slugPath = await BuildSlugPathAsync(page);
		return EnrichDto(page, slugPath);
	}

	public async Task DeletePageAsync(int id)
	{
		await repository.SoftDeleteRecursiveAsync(id);
	}

	public async Task MovePageAsync(int id, int? newParentId, int newSortOrder)
	{
		var page = await repository.GetByIdAsync(id)
			?? throw new InvalidOperationException($"Wiki page {id} not found.");

		var oldParentId = page.ParentId;

		await repository.MovePageAsync(id, newParentId, newSortOrder);
		await repository.ReorderSiblingsAsync(newParentId, id, newSortOrder);

		if (oldParentId != newParentId)
		{
			await repository.ReorderSiblingsSequentialAsync(oldParentId, id);
		}

		await repository.SaveChangesAsync();
	}

	public async Task<List<WikiPageVersionDto>> GetPageVersionsAsync(int pageId)
	{
		var versions = await repository.GetVersionsByPageIdAsync(pageId);

		return versions.Select(v => new WikiPageVersionDto
		{
			Id = v.Id,
			VersionNumber = v.VersionNumber,
			Title = v.Title,
			Content = v.Content,
			ContentHtml = htmlRenderer.RenderHtml(v.Content),
			CreatedAt = v.CreatedAt,
			CreatedBy = v.CreatedBy
		}).ToList();
	}

	public async Task<WikiPageDto> RevertToVersionAsync(int pageId, int versionId)
	{
		var version = await repository.GetVersionByIdAsync(versionId)
			?? throw new InvalidOperationException($"Wiki page version {versionId} not found.");

		var slug = GenerateSlug(version.Title);

		// XSS: revert content comes from version.Content; sanitise + strip.
		var sanitisedHtml = htmlRenderer.RenderHtml(version.Content) ?? string.Empty;
		var bodyPlainText = htmlRenderer.StripTagsToPlainText(sanitisedHtml);

		await repository.UpdatePageAsync(pageId, version.Title, version.Content, slug, bodyPlainText);

		var maxVersion = await repository.GetMaxVersionNumberAsync(pageId);
		await repository.AddVersionAsync(pageId, version.Title, version.Content, maxVersion + 1);

		var page = await repository.GetByIdAsync(pageId)
			?? throw new InvalidOperationException($"Wiki page {pageId} not found.");

		var slugPath = await BuildSlugPathAsync(page);
		return EnrichDto(page, slugPath);
	}

	private async Task<string> BuildSlugPathAsync(WikiPageDto page) =>
		await BuildSlugPathAsync(page, includeDeleted: false);

	private async Task<string> BuildSlugPathAsync(WikiPageDto page, bool includeDeleted)
	{
		var slugs = new List<string> { page.Slug };
		var currentParentId = page.ParentId;
		var depth = 0;

		while (currentParentId.HasValue && depth < MaxDepth)
		{
			var parent = includeDeleted
				? await repository.GetByIdIncludingDeletedAsync(currentParentId.Value)
				: await repository.GetByIdAsync(currentParentId.Value);
			if (parent == null) break;
			slugs.Insert(0, parent.Slug);
			currentParentId = parent.ParentId;
			depth++;
		}

		return string.Join("/", slugs);
	}

	public async Task<List<DeletedWikiPageDto>> GetDeletedPagesAsync()
	{
		var roots = await repository.GetDeletedRootsAsync();
		var result = new List<DeletedWikiPageDto>(roots.Count);

		foreach (var root in roots)
		{
			var pathStub = new WikiPageDto
			{
				Id = root.Id,
				Slug = root.Slug,
				ParentId = root.ParentId
			};
			var originalPath = await BuildSlugPathAsync(pathStub, includeDeleted: true);
			var descendantCount = await repository.CountDeletedDescendantsAsync(root.Id);

			result.Add(new DeletedWikiPageDto
			{
				Id = root.Id,
				Title = root.Title,
				OriginalSlugPath = originalPath,
				DeletedAt = root.DeletedAt,
				DeletedBy = root.DeletedBy,
				DescendantCount = descendantCount
			});
		}

		return result
			.OrderByDescending(r => r.DeletedAt)
			.ThenBy(r => r.Title)
			.ToList();
	}

	public async Task<List<WikiParentOptionDto>> GetAvailableParentsAsync()
	{
		var pages = await repository.GetLivePagesForParentPickerAsync();
		var lookup = pages.ToLookup(p => p.ParentId);
		var options = new List<WikiParentOptionDto>();

		void Walk(int? parentId, string parentPath, int depth)
		{
			if (depth >= MaxDepth) return;

			foreach (var page in lookup[parentId].OrderBy(p => p.SortOrder).ThenBy(p => p.Title))
			{
				var slugPath = string.IsNullOrEmpty(parentPath) ? page.Slug : $"{parentPath}/{page.Slug}";
				options.Add(new WikiParentOptionDto
				{
					Id = page.Id,
					Title = page.Title,
					SlugPath = slugPath,
					Depth = depth
				});
				Walk(page.Id, slugPath, depth + 1);
			}
		}

		Walk(null, string.Empty, 0);
		return options;
	}

	public async Task<WikiPageDto> RestorePageAsync(int rootId, int? newParentId)
	{
		var root = await repository.GetByIdIncludingDeletedAsync(rootId)
			?? throw new InvalidOperationException($"Wiki page {rootId} not found.");

		if (newParentId.HasValue)
		{
			var parent = await repository.GetByIdAsync(newParentId.Value)
				?? throw new InvalidOperationException(
					$"Target parent {newParentId.Value} is not available (missing or deleted).");
		}

		var slug = root.Slug;
		if (await repository.SlugExistsAsync(slug, newParentId))
			slug = $"{slug}-{DateTime.UtcNow.Ticks}";

		var maxSortOrder = await repository.GetMaxSortOrderAsync(newParentId);
		var sortOrder = maxSortOrder + 1;

		// XSS: recompute BodyPlainText even though Content is unchanged —
		// idempotent on current behaviour, defensive against future restore-path mutations.
		var sanitisedHtml = htmlRenderer.RenderHtml(root.Content) ?? string.Empty;
		var bodyPlainText = htmlRenderer.StripTagsToPlainText(sanitisedHtml);

		await repository.ExecuteInTransactionAsync(async () =>
		{
			await repository.RestoreSubtreeAsync(rootId, newParentId, slug, sortOrder, bodyPlainText);
			await repository.SaveChangesAsync();
		});

		var restored = await repository.GetByIdAsync(rootId)
			?? throw new InvalidOperationException($"Wiki page {rootId} not found after restore.");

		var slugPath = await BuildSlugPathAsync(restored);
		return EnrichDto(restored, slugPath);
	}

	private WikiPageDto EnrichDto(WikiPageDto page, string slugPath) => new()
	{
		Id = page.Id,
		Title = page.Title,
		Slug = page.Slug,
		SlugPath = slugPath,
		Content = page.Content,
		ContentHtml = htmlRenderer.RenderHtml(page.Content),
		ParentId = page.ParentId,
		SortOrder = page.SortOrder
	};

	private static string GenerateSlug(string title)
	{
		var slug = title.ToLowerInvariant();
		slug = SlugInvalidChars().Replace(slug, "");
		slug = SlugWhitespace().Replace(slug, "-");
		slug = SlugMultipleDashes().Replace(slug, "-");
		return slug.Trim('-');
	}

	[GeneratedRegex(@"[^a-z0-9\s-]")]
	private static partial Regex SlugInvalidChars();

	[GeneratedRegex(@"\s+")]
	private static partial Regex SlugWhitespace();

	[GeneratedRegex(@"-{2,}")]
	private static partial Regex SlugMultipleDashes();
}
