using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public sealed class CustomField
    {
        [JsonProperty("id")]
        public long Id { get; init; }

        [JsonProperty("value")]
        public object? Value { get; init; }
    }
}
