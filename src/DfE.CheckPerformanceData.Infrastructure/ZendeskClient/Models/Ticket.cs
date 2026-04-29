using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class Ticket
    {
        [JsonProperty("id")]
        public long Id { get; init; }

        [JsonProperty("subject")]
        public string Subject { get; init; } = string.Empty;

        [JsonProperty("status")]
        public string Status { get; init; } = string.Empty;

        [JsonProperty("type")]
        public string? Type { get; init; }

        [JsonProperty("priority")]
        public string? Priority { get; init; }

        [JsonProperty("requester_id")]
        public long RequesterId { get; init; }

        [JsonProperty("assignee_id")]
        public long? AssigneeId { get; init; }

        [JsonProperty("group_id")]
        public long? GroupId { get; init; }

        [JsonProperty("created_at")]
        public DateTime? CreatedAt { get; init; }

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; init; }

        [JsonProperty("description")]
        public string? Description { get; init; }

        [JsonProperty("tags")]
        public string[] Tags { get; init; } = Array.Empty<string>();

        [JsonProperty("custom_fields")]
        public List<CustomField>? CustomFields { get; init; }

        [JsonProperty("fields")]
        public List<CustomField>? Fields { get; init; }

        /// <summary>
        /// Combines all custom fields from both CustomFields and Fields lists for easy access.
        /// </summary>
        public List<CustomField> AllCustomFields =>
            (CustomFields ?? new List<CustomField>())
            .Concat(Fields ?? new List<CustomField>())
            .ToList();

        //[JsonProperty("description_fields")]
        //public Dictionary<string, string>? DescriptionFields { get; init; }

        // Example of lazy loading embedded fields for performance optimization.
        [JsonIgnore]
        private Dictionary<string, string>? _descriptionFieldsCache;

        /// <summary>
        /// Lazy-loaded dictionary containing parsed description fields.
        /// It parses the raw Description string into key-value pairs.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string>? DescriptionFields
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
        public string? DescriptionReasonForRemoval => DescriptionFields?.GetValueOrDefault("request_StudentRemoveCategoryUnderscore");

        /// <summary>
        /// Gets the value for the 'request_Outcome' field, or null if not found.
        /// </summary>
        public string? DescriptionOutcome => DescriptionFields?.GetValueOrDefault("request_Outcome");

        /// <summary>
        /// Gets the value for the 'request_StudentDfEEN' field, or null if not found.
        /// </summary>
        public string? DescriptionStudentDfEEN => DescriptionFields?.GetValueOrDefault("request_StudentDfEEN");

        /// <summary>
        /// Gets the value for the 'request_StudentUPN' field, or null if not found.
        /// </summary>
        public string? DescriptionStudentUPN => DescriptionFields?.GetValueOrDefault("request_StudentUPN");

        /// <summary>
        /// Retrieves the value of the 'UPN' custom field based on provided metadata.
        /// </summary>
        /// <param name="meta">List of custom field metadata.</param>
        /// <returns>The value of the UPN custom field, or null if not found.</returns>
        public string? CustomFieldsStudentUPN(IEnumerable<CustomFieldMetaData> meta)
        {
            var field = meta.FirstOrDefault(x => x.Title == "UPN");
            if (field == null)
                return null;

            return AllCustomFields.FirstOrDefault(x => x.Id == field.Id)?.Value?.ToString();
        }

        /// <summary>
        /// Retrieves the value of the custom field related to the outcome.
        /// </summary>
        public string? CustomFieldsOutcome
        {
            get
            {
                const long OutcomeCustomFieldId = 19056253670034;
                return AllCustomFields.FirstOrDefault(x => x.Id == OutcomeCustomFieldId)?.Value?.ToString();
            }
        }
    }
}
