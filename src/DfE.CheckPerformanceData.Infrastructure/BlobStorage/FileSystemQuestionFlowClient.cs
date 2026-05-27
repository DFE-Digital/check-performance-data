using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class FileSystemQuestionFlowClient(string contentRootPath) : IQuestionFlowBlobClient
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

    public Task UploadConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType, string json)
        => throw new NotSupportedException("Question flow upload is not supported in filesystem mode.");
}
