using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>A file size for people to read: bytes under 1 KB, then KB or MB to one decimal place.</summary>
public static class MediaFileSize
{
    public static string Format(long bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        if (bytes < kb) return bytes == 1 ? "1 byte" : $"{bytes} bytes";
        if (bytes < mb) return (bytes / kb).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / mb).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
