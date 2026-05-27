using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public sealed class ZendeskSettings
    {
        public const string SectionName = "ZendeskSettings";
        public required string Subdomain { get; set; }
        public required string Domain { get; set; }
        public required string Email { get; set; }
        public required string ApiToken { get; set; }

        /// <summary>
        /// Validates that credentials are configured with actual values (not placeholders).
        /// </summary>
        [Obsolete("This method is deprecated; use the DI-based configuration validation instead.")]
        public void ValidateOrWarn()
        {
            var placeholders = new[] { "the subdomain", "the domain", "[PLACE THESE IN YOUR USER SECRETS]" };
            var invalidValues = new List<string>();

            if (placeholders.Contains(Subdomain, StringComparer.OrdinalIgnoreCase))
                invalidValues.Add(nameof(Subdomain));
            if (placeholders.Contains(Domain, StringComparer.OrdinalIgnoreCase))
                invalidValues.Add(nameof(Domain));
            if (placeholders.Contains(ApiToken, StringComparer.OrdinalIgnoreCase))
                invalidValues.Add(nameof(ApiToken));

            // Validation is performed via DI options validation in DependencyManager.AddZendeskApiClient().
        }
    }

    public sealed class PollySettings
    {
        public const string SectionName = "PollySettings";
        public int MaxRetryAttempts { get; set; } = 3;
        public int BaseDelayMilliseconds { get; set; } = 1000;
        public int JitterMilliseconds { get; set; } = 500;
    }

    /// <summary>
    /// Configuration for the Schools Checking Exercise Zendesk integration.
    /// View title, GroupId and BrandId should be set per environment since they can differ between instances.
    /// </summary>
    public sealed class SchoolCheckingExerciseSettings
    {
        public const string SectionName = "SchoolCheckingExercise";
        public required string TargetViewTitle { get; set; }

        /// <summary>
        /// The Zendesk group ID that new tickets should be assigned to.
        /// This is environment-specific (different IDs per instance).
        /// </summary>
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
        public long GroupId { get; set; }

        /// <summary>
        /// The Zendesk brand ID for the ticket brand.
        /// This is environment-specific (different IDs per instance).
        /// </summary>
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
        public long BrandId { get; set; }

        /// <summary>
        /// Optional Zendesk custom field ID for the rules-engine decision status
        /// (AutoApproved / AutoRejected / Scrutiny). When 0 or unset, the field
        /// is omitted from the ticket — useful before the field has been
        /// provisioned in Zendesk admin.
        /// </summary>
        public long DecisionStatusCustomFieldId { get; set; }

        /// <summary>
        /// Optional Zendesk custom field ID for the engine outcome key
        /// (e.g. "ElectiveHomeEducation"). Omitted when 0.
        /// </summary>
        public long OutcomeKeyCustomFieldId { get; set; }

        /// <summary>
        /// Optional Zendesk custom field ID for the matched rule branch id
        /// (e.g. "EHE-KS4"). Omitted when 0.
        /// </summary>
        public long MatchedRuleIdCustomFieldId { get; set; }
    }
}
