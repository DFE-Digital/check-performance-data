using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class AppSettingsSecretsTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.development.json")]
    public void GoogleTagManager_AuthKey_IsNotCommittedInPlaintext(string fileName)
    {
        var json = ReadAppSettings(fileName);
        if (!json.RootElement.TryGetProperty("GoogleTagManager", out var gtm)) return;

        if (gtm.TryGetProperty("AuthKey", out var authKey))
        {
            Assert.True(
                string.IsNullOrEmpty(authKey.GetString()),
                $"{fileName} contains a non-empty GoogleTagManager:AuthKey. Move the value to an environment variable (GoogleTagManager__AuthKey).");
        }
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.development.json")]
    public void GoogleTagManager_PreviewId_IsNotCommittedInPlaintext(string fileName)
    {
        var json = ReadAppSettings(fileName);
        if (!json.RootElement.TryGetProperty("GoogleTagManager", out var gtm)) return;

        if (gtm.TryGetProperty("PreviewId", out var previewId))
        {
            Assert.True(
                string.IsNullOrEmpty(previewId.GetString()),
                $"{fileName} contains a non-empty GoogleTagManager:PreviewId. Move the value to an environment variable (GoogleTagManager__PreviewId).");
        }
    }

    private static JsonDocument ReadAppSettings(string fileName, [CallerFilePath] string testFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Makefile")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Could not locate repository root.");

        var path = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.Web", fileName);
        var text = File.ReadAllText(path);
        return JsonDocument.Parse(text);
    }
}
