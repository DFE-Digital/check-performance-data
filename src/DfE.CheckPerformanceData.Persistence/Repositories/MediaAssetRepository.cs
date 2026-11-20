using System.Linq.Expressions;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// The media library's records. Versions are only ever inserted: there is no update path for a
// MediaAssetVersion, so a page version that recorded version N always finds the same file.
// Every write is one SaveChangesAsync, so each is atomic without an explicit transaction, and the
// generic audit capture records it.
public sealed class MediaAssetRepository(IPortalDbContext context, ICurrentUserService currentUser) : IMediaAssetRepository
{
    public async Task<MediaAssetDto> CreateAsync(NewMediaAsset asset, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset.Slug);
        if (asset.Id == Guid.Empty) throw new ArgumentException("A media item needs an id.", nameof(asset));

        var now = DateTime.UtcNow;
        var entity = new MediaAsset
        {
            Id = asset.Id,
            FileName = asset.FileName.Trim(),
            Slug = asset.Slug.Trim(),
            CurrentVersion = 1,
            CreatedAtUtc = now,
            CreatedBy = currentUser.Email,
            UpdatedAtUtc = now,
            UpdatedBy = currentUser.Email
        };
        Apply(entity, asset.Metadata);
        var first = NewVersion(asset.Id, 1, asset.FirstVersion, now, currentUser.Email);
        entity.Versions.Add(first);

        context.MediaAssets.Add(entity);
        await context.SaveChangesAsync(ct);
        return ToDto(entity, first);
    }

    public async Task<MediaAssetVersionDto> AddVersionAsync(
        Guid assetId, int version, NewMediaAssetVersion file, CancellationToken ct = default)
    {
        var asset = await context.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, ct)
            ?? throw new KeyNotFoundException($"Media item {assetId} not found.");
        if (version != asset.CurrentVersion + 1) throw new MediaVersionConflictException(assetId, version);

        var now = DateTime.UtcNow;
        var row = NewVersion(assetId, version, file, now, currentUser.Email);
        context.MediaAssetVersions.Add(row);
        asset.CurrentVersion = version;
        asset.UpdatedAtUtc = now;
        asset.UpdatedBy = currentUser.Email;

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another replace saved this version number first (the composite key refused ours).
            // Clear the tracker: it still holds the refused version, the changed image and the
            // audit rows collected for them, which a later save on this context would write.
            context.ChangeTracker.Clear();
            throw new MediaVersionConflictException(assetId, version, ex);
        }

        return ToDto(row);
    }

    public async Task<MediaAssetDto?> GetAsync(Guid assetId, CancellationToken ct = default) =>
        await WithLatest(context.MediaAssets.AsNoTracking().Where(a => a.Id == assetId)).FirstOrDefaultAsync(ct);

    public async Task<MediaAssetVersionDto?> GetVersionAsync(Guid assetId, int version, CancellationToken ct = default) =>
        await context.MediaAssetVersions.AsNoTracking()
            .Where(v => v.AssetId == assetId && v.Version == version)
            .Select(VersionProjection)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MediaAssetVersionDto>> GetVersionsAsync(Guid assetId, CancellationToken ct = default) =>
        await context.MediaAssetVersions.AsNoTracking()
            .Where(v => v.AssetId == assetId)
            .OrderByDescending(v => v.Version)
            .Select(VersionProjection)
            .ToListAsync(ct);

    public async Task<MediaAssetPage> ListAsync(MediaAssetQuery query, CancellationToken ct = default)
    {
        var assets = context.MediaAssets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = "%" + EscapeLike(query.Search.Trim()) + "%";
            assets = assets.Where(a =>
                EF.Functions.ILike(a.FileName, pattern, "\\")
                || EF.Functions.ILike(a.DefaultAltText ?? "", pattern, "\\")
                || EF.Functions.ILike(a.Caption ?? "", pattern, "\\")
                || EF.Functions.ILike(a.Credit ?? "", pattern, "\\")
                || a.Tags.Any(t => EF.Functions.ILike(t, pattern, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim().ToLower();
            assets = assets.Where(a => a.Tags.Any(t => t.ToLower() == tag));
        }

        if (query.MissingAltText)
            assets = assets.Where(a => a.DefaultAltText == null || a.DefaultAltText.Trim() == "");

        if (query.UnusedOnly)
            assets = assets.Where(a => !context.MediaUsages.Any(u => u.AssetId == a.Id));

        var pageSize = Math.Max(1, query.PageSize);
        var total = await assets.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var items = await WithLatest(assets
                .OrderByDescending(a => a.UpdatedAtUtc).ThenBy(a => a.Id)
                .Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(ct);

        return new MediaAssetPage(items, total, page, pageSize);
    }

    public async Task<bool> UpdateMetadataAsync(Guid assetId, MediaAssetMetadata metadata, CancellationToken ct = default)
    {
        var asset = await context.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId, ct);
        if (asset is null) return false;

        Apply(asset, metadata);
        asset.UpdatedAtUtc = DateTime.UtcNow;
        asset.UpdatedBy = currentUser.Email;
        await context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MediaAssetDto>> FindBySha256Async(string sha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return [];
        var sha = sha256.Trim().ToLowerInvariant();
        return await WithLatest(context.MediaAssets.AsNoTracking()
                .Where(a => a.Versions.Any(v => v.Sha256 == sha))
                .OrderByDescending(a => a.UpdatedAtUtc).ThenBy(a => a.Id))
            .ToListAsync(ct);
    }

    // Blank text is stored as null, so "missing alt text" has one meaning. Tags are trimmed, blank
    // ones dropped and repeats (ignoring case) removed, keeping the first spelling.
    private static void Apply(MediaAsset asset, MediaAssetMetadata metadata)
    {
        asset.DefaultAltText = NullIfBlank(metadata.DefaultAltText);
        asset.Caption = NullIfBlank(metadata.Caption);
        asset.Credit = NullIfBlank(metadata.Credit);
        asset.Tags = metadata.Tags
            .Select(t => t?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MediaAssetVersion NewVersion(Guid assetId, int version, NewMediaAssetVersion file, DateTime now, string? uploadedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file.BlobKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(file.ContentType);
        var sha = file.Sha256?.Trim().ToLowerInvariant() ?? "";
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new ArgumentException("Sha256 must be 64 hexadecimal characters.", nameof(file));

        return new MediaAssetVersion
        {
            AssetId = assetId,
            Version = version,
            BlobKey = file.BlobKey,
            ContentType = file.ContentType,
            Width = file.Width,
            Height = file.Height,
            SizeBytes = file.SizeBytes,
            Sha256 = sha,
            IsAnimated = file.IsAnimated,
            UploadedAtUtc = now,
            UploadedBy = uploadedBy
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // Each image with the row of its current version, projected in SQL.
    private static IQueryable<MediaAssetDto> WithLatest(IQueryable<MediaAsset> assets) =>
        assets.Select(a => new
        {
            Asset = a,
            Latest = a.Versions.First(v => v.Version == a.CurrentVersion)
        })
        .Select(x => new MediaAssetDto(
            x.Asset.Id, x.Asset.FileName, x.Asset.Slug, x.Asset.DefaultAltText, x.Asset.Caption, x.Asset.Credit,
            x.Asset.Tags, x.Asset.CurrentVersion,
            new MediaAssetVersionDto(
                x.Latest.AssetId, x.Latest.Version, x.Latest.BlobKey, x.Latest.ContentType, x.Latest.Width,
                x.Latest.Height, x.Latest.SizeBytes, x.Latest.Sha256, x.Latest.IsAnimated,
                x.Latest.UploadedAtUtc, x.Latest.UploadedBy),
            x.Asset.CreatedAtUtc, x.Asset.CreatedBy, x.Asset.UpdatedAtUtc, x.Asset.UpdatedBy));

    private static readonly Expression<Func<MediaAssetVersion, MediaAssetVersionDto>> VersionProjection = v =>
        new MediaAssetVersionDto(v.AssetId, v.Version, v.BlobKey, v.ContentType, v.Width, v.Height,
            v.SizeBytes, v.Sha256, v.IsAnimated, v.UploadedAtUtc, v.UploadedBy);

    private static MediaAssetVersionDto ToDto(MediaAssetVersion v) =>
        new(v.AssetId, v.Version, v.BlobKey, v.ContentType, v.Width, v.Height,
            v.SizeBytes, v.Sha256, v.IsAnimated, v.UploadedAtUtc, v.UploadedBy);

    private static MediaAssetDto ToDto(MediaAsset a, MediaAssetVersion latest) =>
        new(a.Id, a.FileName, a.Slug, a.DefaultAltText, a.Caption, a.Credit, a.Tags.ToList(), a.CurrentVersion,
            ToDto(latest), a.CreatedAtUtc, a.CreatedBy, a.UpdatedAtUtc, a.UpdatedBy);
}
