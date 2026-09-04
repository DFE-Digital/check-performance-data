using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Infrastructure.QuestionFlow;

/// <summary>
/// Serves question flow configs from the files that ship inside the release image, at
/// <c>{contentRootPath}/Data/QuestionFlows/{WhatToChange}_{CheckingWindowType}.json</c>. This is
/// the only source in every environment — see <c>docs/question-flow-deployment.md</c> for why the
/// blob-backed source and its Development-gated seeding step were removed.
///
/// A missing file returns null (the caller decides what an absent flow means); malformed JSON is
/// allowed to throw, because a config that ships broken is a build-time mistake rather than a
/// runtime condition to absorb.
/// </summary>
public sealed class FileSystemQuestionFlowClient(string contentRootPath) : IQuestionFlowConfigSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var path = Path.Combine(contentRootPath, "Data", "QuestionFlows", $"{whatToChange}_{checkingWindowType}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<QuestionFlowConfig>(json, JsonOptions);
    }
}
