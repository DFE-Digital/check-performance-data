using System;
using System.Collections.Generic;
using System.Linq;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketDto
    {
        public long Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Priority { get; set; } = string.Empty;

        public long RequesterId { get; set; }

        public long? AssigneeId { get; set; }

        public long? GroupId { get; set; }

        public DateTime? CreatedAt { get; set; }

        public string? UpdatedAt { get; set; }

        public string Description { get; set; } = string.Empty;

        public string[] Tags { get; set; } = Array.Empty<string>();

        public List<CustomFieldDto> CustomFields { get; set; } = new();

        public List<CustomFieldDto> Fields { get; set; } = new();

        public List<CustomFieldDto> AllCustomFields { get; set; } = new();

        /// <summary>
        /// Dictionary of parsed key-value pairs extracted from the ticket description.
        /// Populated by the ZendeskService when fetching ticket details.
        /// </summary>
        public Dictionary<string, string> DescriptionFields { get; set; } = new();

        // Parsed description fields
        public string? DescriptionReasonForRemoval { get; set; }

        public string? DescriptionOutcome { get; set; }

        public string? DescriptionStudentDfEEN { get; set; }

        public string? DescriptionStudentUPN { get; set; }

        public string? CustomFieldsOutcome { get; set; }

        /// <summary>
        /// Retrieves the value of the 'UPN' custom field based on provided metadata.
        /// </summary>
        /// <param name="meta">List of custom field metadata.</param>
        /// <returns>The value of the UPN custom field, or null if not found.</returns>
        public string? CustomFieldsStudentUPN(List<CustomFieldMetaDataDto> meta)
        {
            var field = meta.FirstOrDefault(x => x.Title == "UPN");
            if (field == null)
                return null;

            return AllCustomFields.FirstOrDefault(x => x.Id == field.Id)?.Value?.ToString();
        }

        public string? CustomFieldsByName(List<CustomFieldMetaDataDto> meta, string fieldName)
        {
            var field = meta.FirstOrDefault(x => x.Title == fieldName);
            if (field == null)
                return null;

            return AllCustomFields.FirstOrDefault(x => x.Id == field.Id)?.Value?.ToString();
        }
    }
}
