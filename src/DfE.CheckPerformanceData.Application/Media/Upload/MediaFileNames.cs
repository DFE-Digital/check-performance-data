using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>
/// The file name and slug a library image is stored under. A name from a browser or a bundle is
/// untrusted, so only its last part is kept, cut to fit the column.
/// </summary>
public static class MediaFileNames
{
    private const int MaxFileNameLength = 255;
    private const int MaxSlugLength = 200;
    private const string FallbackName = "image";

    /// <summary>The last part of the name, with any folder path removed, cut to fit.</summary>
    public static string Safe(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/').Trim());
        if (name.Length == 0) return FallbackName;
        if (name.Length <= MaxFileNameLength) return name;

        var extension = Path.GetExtension(name);
        if (extension.Length >= MaxFileNameLength / 2) extension = "";
        return name[..(MaxFileNameLength - extension.Length)] + extension;
    }

    public static string Slug(string fileName)
    {
        var slug = WikiSlug.Generate(Path.GetFileNameWithoutExtension(fileName));
        if (slug.Length > MaxSlugLength) slug = slug[..MaxSlugLength].TrimEnd('-');
        return slug.Length == 0 ? FallbackName : slug;
    }
}
