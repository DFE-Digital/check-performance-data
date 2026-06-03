using System.Text;
using System.Text.Json;
using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.BlobStorage;

public sealed class RequestQueueClient : IRequestQueueClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly Azure.Storage.Queues.QueueClient _queueClient;

    public RequestQueueClient(
        QueueServiceClient queueServiceClient,
        IOptions<RulesEngineOptions> options)
    {
        var queueName = options.Value.QueueName;
        _queueClient = queueServiceClient.GetQueueClient(queueName);
    }

    public async Task EnqueueRequestAsync(RequestDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await _queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
    }
}