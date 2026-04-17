using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class Ticket
    {

        // example of lazy loading embedded fields
        [JsonIgnore]
        private Dictionary<string, string> _descriptionFieldsCache;

        [JsonIgnore]
        public Dictionary<string, string> DescriptionFields
        {
            get
            {
                if (_descriptionFieldsCache != null)
                    return _descriptionFieldsCache;

                if (string.IsNullOrWhiteSpace(Description))
                    return _descriptionFieldsCache = new Dictionary<string, string>();

     
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

                                // Normalize "null" (string) to actual null
                                return string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                                    ? null
                                    : value;
                            }
                        );
            }
        }
        [JsonIgnore]
        public string? DescriptionReasonForRemoval =>
            DescriptionFields.TryGetValue("request_StudentRemoveCategoryUnderscore", out var v)
            ? v
            : null;
        [JsonIgnore]
        public string? DescriptionOutcome => DescriptionFields.TryGetValue("request_Outcome", out var v)
            ? v
            : null;
        [JsonIgnore]
        public string? DescriptionStudentDfEEN => DescriptionFields.TryGetValue("request_StudentDfEEN", out var v)
            ? v
            : null;
        
        public string? CustomFieldsStudentUPN(List<CustomFieldMetaData> meta)
        {
            // the long ids of the fields vary from environment to environment so cant be used as constants. Using environment variables may be a better approach.
            var field = meta.FirstOrDefault(x => x.Title == "UPN");
            if (field == null)
                return null;
            return AllCustomFields.FirstOrDefault(x => x.Id == field.Id)?.Value?.ToString() ?? null;
        }

        [JsonIgnore]
        public string? DescriptionStudentUPN => DescriptionFields.TryGetValue("request_StudentUPN", out var v)
            ? v
            : null;

        //private const long AutoRejectedId = 19056253670034;
        //private const long AutoApprovedId = 19056253670034;
        private const long OutcomeCustomFieldId = 19056253670034;
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


        [JsonProperty("fields")] // legacy api field
        public List<CustomField> Fields { get; set; } = new List<CustomField>();

        public List<CustomField> AllCustomFields => CustomFields.Concat(Fields).ToList();



    }

}