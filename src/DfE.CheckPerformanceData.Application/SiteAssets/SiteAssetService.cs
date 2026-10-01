using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Application.SiteAssets;

// The custom CSS and JavaScript an administrator has stored for the site, with the switches
// that turn each one off. Serves* is what the site actually emits: content that is switched off,
// or blank, is never linked or served. The version of each asset is the UTC time it was last
// saved (yyyyMMddHHmmss), used to cache-bust the asset URL so an edit reaches every browser
// without a hard refresh, and readable from view-source when troubleshooting. It is empty for
// an asset saved before timestamps were kept.
public sealed record SiteAssetContent(
    string Css, string Js, bool CssEnabled, bool JsEnabled, string CssSavedAt = "", string JsSavedAt = "")
{
    public const string VersionFormat = "yyyyMMddHHmmss";

    public bool ServesCss => CssEnabled && !string.IsNullOrWhiteSpace(Css);
    public bool ServesJs => JsEnabled && !string.IsNullOrWhiteSpace(Js);
    public string CssVersion => CssSavedAt;
    public string JsVersion => JsSavedAt;
}

public sealed record SiteAssetSaveResult(bool Succeeded, string? Error = null);

public interface ISiteAssetService
{
    Task<SiteAssetContent> GetAsync();
    Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content);
}

// Reads and writes the site assets through the settings store, so every change goes through the
// same audited write path as any other setting. Reads are cached briefly because the layout asks
// on every page view; a save drops the cache on this instance and other instances catch
// up when the entry expires.
public sealed class SiteAssetService(ISettingService settings, IMemoryCache cache, TimeProvider? clock = null) : ISiteAssetService
{
    public const int MaxLength = 200_000;
    private const string CacheKey = "site-assets";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<SiteAssetContent> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out SiteAssetContent? cached) && cached is not null)
            return cached;

        var content = new SiteAssetContent(
            await settings.GetValueAsync(SettingKeys.SiteCss),
            await settings.GetValueAsync(SettingKeys.SiteJs),
            await settings.GetBoolAsync(SettingKeys.SiteCssEnabled),
            await settings.GetBoolAsync(SettingKeys.SiteJsEnabled),
            await settings.GetValueAsync(SettingKeys.SiteCssSavedAt),
            await settings.GetValueAsync(SettingKeys.SiteJsSavedAt));

        cache.Set(CacheKey, content, CacheFor);
        return content;
    }

    public async Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content)
    {
        if (content.Css.Length > MaxLength)
            return new SiteAssetSaveResult(false, $"The CSS must be {MaxLength:N0} characters or fewer.");
        if (content.Js.Length > MaxLength)
            return new SiteAssetSaveResult(false, $"The JavaScript must be {MaxLength:N0} characters or fewer.");

        // An asset is stamped only when what it serves changed, so the URL (and the browser cache)
        // moves exactly when the output does. What is stored now is read afresh rather than from the
        // cache, because another server may have saved since this one cached it.
        cache.Remove(CacheKey);
        var current = await GetAsync();
        var stamp = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime.ToString(SiteAssetContent.VersionFormat, System.Globalization.CultureInfo.InvariantCulture);

        await settings.SaveAsync(SettingKeys.SiteCss, content.Css);
        await settings.SaveAsync(SettingKeys.SiteJs, content.Js);
        await settings.SaveAsync(SettingKeys.SiteCssEnabled, content.CssEnabled ? "true" : "false");
        await settings.SaveAsync(SettingKeys.SiteJsEnabled, content.JsEnabled ? "true" : "false");
        if (content.Css != current.Css || content.CssEnabled != current.CssEnabled)
            await settings.SaveAsync(SettingKeys.SiteCssSavedAt, stamp);
        if (content.Js != current.Js || content.JsEnabled != current.JsEnabled)
            await settings.SaveAsync(SettingKeys.SiteJsSavedAt, stamp);

        cache.Remove(CacheKey);
        return new SiteAssetSaveResult(true);
    }
}
