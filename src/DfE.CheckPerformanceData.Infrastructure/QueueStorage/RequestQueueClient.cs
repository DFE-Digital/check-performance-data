using System.Text.Json;
using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Infrastructure.QueueStorage;

/// <summary>
/// Azure Storage Queue implementation of <see cref="IRequestQueueClient"/>.
/// The injected <see cref="QueueClient"/> must use the same queue name and
/// message encoding as the RulesEngineWorker's consumer (Base64,
/// <c>RulesEngineOptions.QueueName</c>); the body is the default
/// System.Text.Json serialization of <see cref="RequestDocument"/>, which
/// <c>RequestDocumentParser</c> reads back (round-trip pinned in tests).
/// </summary>
public sealed class RequestQueueClient(QueueClient queueClient) : IRequestQueueClient
{
    public async Task EnqueueRequestAsync(RequestDocument document)
    {
        await queueClient.CreateIfNotExistsAsync();
        await queueClient.SendMessageAsync(JsonSerializer.Serialize(document));
    }
}
