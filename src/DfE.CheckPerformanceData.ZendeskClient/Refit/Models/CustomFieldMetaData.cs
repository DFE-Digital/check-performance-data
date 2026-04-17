using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class CustomFieldMetaData
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("key")]
        public string Key { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("raw_title")]
        public string RawTitle { get; set; }

        [JsonProperty("raw_description")]
        public string RawDescription { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("system")]
        public bool System { get; set; }

        [JsonProperty("regexp_for_validation")]
        public string RegexpForValidation { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        // 🔥 This is the important one
        [JsonProperty("custom_field_options")]
        public List<CustomFieldOption> CustomFieldOptions { get; set; } = new List<CustomFieldOption>();
    }

    public class CustomFieldOption
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("raw_name")]
        public string RawName { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }
}
