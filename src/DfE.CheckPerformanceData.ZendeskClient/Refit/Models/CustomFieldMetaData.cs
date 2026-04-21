using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    /// <summary>
    /// Represents metadata for a custom field, including its properties and validation patterns.
    /// </summary>
    public class CustomFieldMetaData
    {
        /// <summary>
        /// Gets or sets the URL associated with the custom field.
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for the custom field.
        /// </summary>
        [JsonProperty("id")]
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the type of the custom field.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the key associated with the custom field.
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the title of the custom field.
        /// </summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the description of the custom field.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the raw title of the custom field.
        /// </summary>
        [JsonProperty("raw_title")]
        public string RawTitle { get; set; }

        /// <summary>
        /// Gets or sets the raw description of the custom field.
        /// </summary>
        [JsonProperty("raw_description")]
        public string RawDescription { get; set; }

        /// <summary>
        /// Gets or sets the position of the custom field.
        /// </summary>
        [JsonProperty("position")]
        public int Position { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the custom field is active.
        /// </summary>
        [JsonProperty("active")]
        public bool Active { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the custom field is a system field.
        /// </summary>
        [JsonProperty("system")]
        public bool System { get; set; }

        /// <summary>
        /// Gets or sets the regular expression for validating the custom field value.
        /// </summary>
        [JsonProperty("regexp_for_validation")]
        public string RegexpForValidation { get; set; }

        /// <summary>
        /// Gets or sets the creation date of the custom field.
        /// </summary>
        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Gets or sets the last update date of the custom field.
        /// </summary>
        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Gets or sets a list of custom field options.
        /// </summary>
        [JsonProperty("custom_field_options")]
        public List<CustomFieldOption> CustomFieldOptions { get; set; } = new List<CustomFieldOption>();
    }


    public class CustomFieldOption
    {
        // Unique identifier for the instance
        [JsonProperty("id")]
        public long Id { get; set; }

        // Human-readable name of the option
        [JsonProperty("name")]
        public string Name { get; set; }

        // Raw name used for internal processing
        [JsonProperty("raw_name")]
        public string RawName { get; set; }

        // Value of the option
        [JsonProperty("value")]
        public string Value { get; set; }
    }

}
