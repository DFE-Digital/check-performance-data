using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class Ticket
    {

        // Example of lazy loading embedded fields for performance optimization.
        [JsonIgnore]
        private Dictionary<string, string> _descriptionFieldsCache;

        /// <summary>
        /// Lazy-loaded dictionary containing parsed description fields.
        /// It parses the raw Description string into key-value pairs.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> DescriptionFields
        {
            get
            {
                // Check if the fields have already been calculated (lazy loading implementation).
                if (_descriptionFieldsCache != null)
                    return _descriptionFieldsCache;

                // If the description is empty, initialize an empty cache.
                if (string.IsNullOrWhiteSpace(Description))
                    return _descriptionFieldsCache = new Dictionary<string, string>();

                // Process the Description string: split by newline, then split key/value pairs, and normalize values.
                return _descriptionFieldsCache =
                    Description
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split(':', 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(
                            parts => parts[0].Trim(),
                            parts =>
                            {
                                var value = parts[1].Trim();

                                // Normalize "null" (string) representation found in the source data to actual null.
                                return string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : value;
                            }
                        );
            }
        }

        /// <summary>
        /// Gets the value for the 'request_StudentRemoveCategoryUnderscore' field, or null if not found.
        /// </summary>
        [JsonIgnore]
        public string? DescriptionReasonForRemoval =>
            DescriptionFields.TryGetValue("request_StudentRemoveCategoryUnderscore", out var v)
            ? v
            : null;

        /// <summary>
        /// Gets the value for the 'request_Outcome' field, or null if not found.
        /// </summary>
        [JsonIgnore]
        public string? DescriptionOutcome => DescriptionFields.TryGetValue("request_Outcome", out var v)
            ? v
            : null;

        /// <summary>
        /// Gets the value for the 'request_StudentDfEEN' field, or null if not found.
        /// </summary>
        [JsonIgnore]
        public string? DescriptionStudentDfEEN => DescriptionFields.TryGetValue("request_StudentDfEEN", out var v)
            ? v
            : null;

        /// <summary>
        /// Retrieves the value of the 'UPN' custom field based on provided metadata.
        /// </summary>
        /// <param name="meta">List of custom field metadata.</param>
        /// <returns>The value of the UPN custom field, or null if not found.</returns>
        public string? CustomFieldsStudentUPN(List<CustomFieldMetaData> meta)
        {
            // Note: The long IDs of custom fields vary by environment, making hardcoding difficult.
            var field = meta.FirstOrDefault(x => x.Title == "UPN");
            if (field == null)
                return null;

            // Find the corresponding value using the custom field ID.
            return AllCustomFields.FirstOrDefault(x => x.Id == field.Id)?.Value?.ToString() ?? null;
        }

        /// <summary>
        /// Gets the value for the 'request_StudentUPN' field, or null if not found.
        /// </summary>
        [JsonIgnore]
        public string? DescriptionStudentUPN => DescriptionFields.TryGetValue("request_StudentUPN", out var v)
            ? v
            : null;

        // Custom field ID used for retrieving specific outcome data.
        private const long OutcomeCustomFieldId = 19056253670034;

        /// <summary>
        /// Retrieves the value of the custom field related to the outcome.
        /// </summary>
        public string? CustomFieldsOutcome { get => this.AllCustomFields.FirstOrDefault(x => x.Id == OutcomeCustomFieldId)?.Value?.ToString(); }

        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("priority")]
        public string Priority { get; set; }

        [JsonProperty("requester_id")]
        public long RequesterId { get; set; }

        [JsonProperty("assignee_id")]
        public long? AssigneeId { get; set; }

        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; set; }

        [JsonProperty("updated_at")]
        public string UpdatedAt { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("tags")]
        public string[]? Tags { get; set; }

        [JsonProperty("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new List<CustomField>();

        /// <summary>
        /// Legacy API field containing custom field data.
        /// </summary>
        [JsonProperty("fields")]
        public List<CustomField> Fields { get; set; } = new List<CustomField>();

        /// <summary>
        /// Combines all custom fields from both CustomFields and Fields lists for easy access.
        /// </summary>
        public List<CustomField> AllCustomFields => CustomFields.Concat(Fields).ToList();

    }

}