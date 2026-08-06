using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Service for resolving Zendesk ticket field IDs by name.
/// Tries configuration first, falls back to querying the Zendesk API.
/// </summary>
public interface IZendeskTicketFieldService
{
    /// <summary>
    /// Gets the field ID from configuration. Returns null if not configured.
    /// </summary>
    long? GetFieldIdFromConfig(string fieldName);

    /// <summary>
    /// Gets all configured field IDs as CustomFieldDto list.
    /// Only returns fields that have IDs configured.
    /// </summary>
    List<CustomFieldDto> GetCustomFieldsFromConfig();

    /// <summary>
    /// Gets all field names defined in constants.
    /// </summary>
    List<string> GetAllFieldNames();

    /// <summary>
    /// Gets or resolves a field ID by name.
    /// Tries configuration first, then falls back to querying Zendesk API.
    /// </summary>
    Task<long?> GetFieldIdAsync(string fieldName);

    /// <summary>
    /// Gets the option value for a dropdown field given the field name and a human-readable option name.
    /// For example: GetOptionValue("Sex", "male") returns "m"
    /// </summary>
    string? GetOptionValue(string fieldName, string optionName);

    /// <summary>
    /// Gets whether AutoRejected decisions should also populate the "Decision Reason - Approved"
    /// field (AutoApproved decisions always populate when mapped; Scrutiny never does).
    /// Backed by <see cref="ZendeskTicketFieldSettings.PopulateDecisionReasonForAutoRejected"/>.
    /// </summary>
    bool PopulateDecisionReasonForAutoRejected { get; }
}

