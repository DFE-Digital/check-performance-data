using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class QuestionFlowBlobClient(BlobServiceClient blobServiceClient) : IQuestionFlowBlobClient
{
    private const string ContainerName = "question-flows";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<QuestionFlowConfig?> GetConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType)
    {
        var container = blobServiceClient.GetBlobContainerClient(ContainerName);
        var blob = container.GetBlobClient($"{whatToChange}_{checkingWindowType}.json");

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<QuestionFlowConfig>(response.Value.Content, JsonOptions);
    }

    public async Task UploadConfigAsync(WhatToChange whatToChange, CheckingWindowType checkingWindowType, string json)
    {
        var container = blobServiceClient.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync();

        var blob = container.GetBlobClient($"{whatToChange}_{checkingWindowType}.json");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }
}
