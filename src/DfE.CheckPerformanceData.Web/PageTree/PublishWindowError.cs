using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// A publish window that was rejected, carried across the redirect back to the edit page so the
/// version's row can show the error beside the field and keep what the editor typed.
/// </summary>
public sealed record PublishWindowError(int VersionId, string? From, string? To, string? FromError, string? ToError)
{
    private const string TempDataKey = "PublishWindowError";

    public void Save(ITempDataDictionary tempData) =>
        tempData[TempDataKey] = JsonSerializer.Serialize(this);

    public static PublishWindowError? Read(ITempDataDictionary tempData) =>
        tempData[TempDataKey] is string json ? JsonSerializer.Deserialize<PublishWindowError>(json) : null;
}
