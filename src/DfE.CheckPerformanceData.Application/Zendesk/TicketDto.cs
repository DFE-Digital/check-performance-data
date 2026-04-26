using System;
using System.Collections.Generic;
using System.Linq;
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketDto
    {
        private Dictionary<string, string> _descriptionFieldsCache;

        // Lazy-loaded dictionary containing parsed description fields.
        public Dictionary<string, string> DescriptionFields { get; set; }
        // Gets the value for the 'request_StudentRemoveCategoryUnderscore' field, or null if not found.
        public string DescriptionReasonForRemoval { get; set; }
        // Gets the value for the 'request_Outcome' field, or null if not found.
        public string DescriptionOutcome { get; set; }
        // Gets the value for the 'request_StudentDfEEN' field, or null if not found.
        public string DescriptionStudentDfEEN { get; set; }
        // Retrieves the value of the 'UPN' custom field based on provided metadata.
        public string CustomFieldsStudentUPN { get; set; }
        // Gets the value for the 'request_StudentUPN' field, or null if not found.
        public string DescriptionStudentUPN { get; set; }
        private const long OutcomeCustomFieldId = 19056253670034;

        // Retrieves the value of the custom field related to the outcome.
        public string CustomFieldsOutcome { get; set; }
        public long Id { get; set; }
        public string Subject { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public string Priority { get; set; }
        public long RequesterId { get; set; }
        public long? AssigneeId { get; set; }
        public long? GroupId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string Description { get; set; }
        public string[] Tags { get; set; }
        public List<CustomFieldDto> CustomFields { get; set; }
        public List<CustomFieldDto> Fields { get; set; }

        // Combines all custom fields from both CustomFields and Fields lists for easy access.
        public List<CustomFieldDto> AllCustomFields { get; set; }
    }
}
