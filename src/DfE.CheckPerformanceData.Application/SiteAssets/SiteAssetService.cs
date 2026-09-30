using System.Security.Cryptography;
using System.Text;
using DfE.CheckPerformanceData.Application.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Application.SiteAssets;

// The custom CSS and JavaScript an administrator has stored for the public site, with the
// switches that turn each one off. Serves* is what the site actually emits: content that is
// switched off, or blank, is never linked or served. The version is a short content hash used
// to cache-bust the asset URL, so an edit reaches every browser without a hard refresh.
public sealed record SiteAssetContent(string Css, string Js, bool CssEnabled, bool JsEnabled)
{
    public bool ServesCss => CssEnabled && !string.IsNullOrWhiteSpace(Css);
    public bool ServesJs => JsEnabled && !string.IsNullOrWhiteSpace(Js);
    public string CssVersion => Hash(Css);
    public string JsVersion => Hash(Js);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
}

public sealed record SiteAssetSaveResult(bool Succeeded, string? Error = null);

public interface ISiteAssetService
{
    Task<SiteAssetContent> GetAsync();
    Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content);
}

// Reads and writes the site assets through the settings store, so every change goes through the
// same audited write path as any other setting. Reads are cached briefly because the layout asks
// on every public page view; a save drops the cache on this instance and other instances catch
// up when the entry expires.
public sealed class SiteAssetService(ISettingService settings, IMemoryCache cache) : ISiteAssetService
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
            await settings.GetBoolAsync(SettingKeys.SiteJsEnabled));

        cache.Set(CacheKey, content, CacheFor);
        return content;
    }

    public async Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content)
    {
        if (content.Css.Length > MaxLength)
            return new SiteAssetSaveResult(false, $"The CSS must be {MaxLength:N0} characters or fewer.");
        if (content.Js.Length > MaxLength)
            return new SiteAssetSaveResult(false, $"The JavaScript must be {MaxLength:N0} characters or fewer.");

        await settings.SaveAsync(SettingKeys.SiteCss, content.Css);
        await settings.SaveAsync(SettingKeys.SiteJs, content.Js);
        await settings.SaveAsync(SettingKeys.SiteCssEnabled, content.CssEnabled ? "true" : "false");
        await settings.SaveAsync(SettingKeys.SiteJsEnabled, content.JsEnabled ? "true" : "false");

        cache.Remove(CacheKey);
        return new SiteAssetSaveResult(true);
    }
}
