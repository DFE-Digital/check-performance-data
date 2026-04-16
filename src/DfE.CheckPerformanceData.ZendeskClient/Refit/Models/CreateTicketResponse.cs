using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class CreateTicketResponse
    {
        [JsonProperty("ticket")]
        public Ticket Ticket { get; set; }
    }

    //public class Ticket
    //{
    //    [JsonProperty("id")]
    //    public long Id { get; set; }

    //    [JsonProperty("subject")]
    //    public string Subject { get; set; }

    //    [JsonProperty("status")]
    //    public string Status { get; set; }

    //    [JsonProperty("priority")]
    //    public string Priority { get; set; }
    //}

    // for list view tickets response, we need more fields to be able to check for attachments and get the attachments
    public class Ticket
    {


        //[JsonIgnore]
        //private readonly Lazy<Dictionary<string, string>> _descriptionFields;

        //public Ticket()
        //{
        //    _descriptionFields = new Lazy<Dictionary<string, string>>(() =>
        //    {
        //        if (string.IsNullOrWhiteSpace(Description))
        //            return new Dictionary<string, string>();

        //        return Description
        //            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        //            .Select(line => line.Split(':', 2))
        //            .Where(parts => parts.Length == 2)
        //            .ToDictionary(
        //                parts => parts[0].Trim(),
        //                parts => parts[1].Trim()
        //            );
        //    });
        //}

        //[JsonIgnore]
        //public Dictionary<string, string> DescriptionFields => _descriptionFields.Value;

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

                //return _descriptionFieldsCache =
                //    Description
                //        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                //        .Select(line => line.Split(':', 2))
                //        .Where(parts => parts.Length == 2)
                //        .ToDictionary(
                //            parts => parts[0].Trim(),
                //            parts => parts[1].Trim()
                //        );
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

        public string? ReasonForRemoval =>
            DescriptionFields.TryGetValue("request_StudentRemoveCategoryUnderscore", out var v)
            ? v
            : null;

        public string? Outcome => DescriptionFields.TryGetValue("request_Outcome", out var v)
            ? v
            : null;
        //nrequest_StudentDfEEN
        public string? StudentDfEEN => DescriptionFields.TryGetValue("request_StudentDfEEN", out var v)
            ? v
            : null;

        //nrequest_StudentUPN
        public string? StudentUPN => DescriptionFields.TryGetValue("request_StudentUPN", out var v)
            ? v
            : null;

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
        public string? Description { get; set; }   // to do seperate create and list models if needed and add property to split this into a list

        [JsonProperty("tags")]
        public string[]? Tags { get; set; }

        [JsonProperty("custom_fields")]
        public List<CustomField> CustomFields { get; set; } = new List<CustomField>();


        [JsonProperty("fields")] // legacy api field
        public List<CustomField> Fields { get; set; } = new List<CustomField>();

        public List<CustomField> AllCustomFields => CustomFields.Concat(Fields).ToList();

        //[JsonIgnore]
        //public Lazy<Dictionary<string, string>> DescriptionFields =>
        //new Lazy<Dictionary<string, string>>(() =>
        //{
        //    if (string.IsNullOrWhiteSpace(Description))
        //        return new Dictionary<string, string>();

        //    return Description
        //        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        //        .Select(line => line.Split(':', 2))
        //        .Where(parts => parts.Length == 2)
        //        .ToDictionary(
        //            parts => parts[0].Trim(),
        //            parts => parts[1].Trim()
        //        );
        //});

        //public string Reason => DescriptionFields.Value["request_StudentRemoveCategoryUnderscore"];


    }

}