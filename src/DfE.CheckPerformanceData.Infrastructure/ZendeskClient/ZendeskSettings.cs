using System;
using System.Collections.Generic;

using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public sealed class ZendeskSettings
    {
        public const string SectionName = "ZendeskSettings";

        public required string Subdomain { get; set; }
        public required string Domain { get; set; }

        // OAuth Client Credentials flow credentials
        // Created in Zendesk Admin Center under Apps and integrations > APIs > OAuth clients
        [Required(AllowEmptyStrings = false)]
        public string ClientId { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false)]
        public string ClientSecret { get; set; } = string.Empty;

        // OAuth scopes required by the integration (space-separated, e.g. "tickets:read tickets:write")
        // Defaults to full read/write if not specified. See:
        // https://developer.zendesk.com/documentation/authentication/oauth-migration/
        public string Scopes { get; set; } = "read write";

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
            if (placeholders.Contains(ClientId, StringComparer.OrdinalIgnoreCase))
                invalidValues.Add(nameof(ClientId));
            if (placeholders.Contains(ClientSecret, StringComparer.OrdinalIgnoreCase))
                invalidValues.Add(nameof(ClientSecret));

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
        /// Null when the environment has no Zendesk group configured — docker-compose forwards
        /// the key with an empty value on a fresh clone, and binding "" to a non-nullable long
        /// threw at host start. Consumers fall back to 0 (Zendesk's "unassigned").
        /// </summary>
        public long? GroupId { get; set; }

        /// <summary>
        /// The Zendesk brand ID for the ticket brand.
        /// This is environment-specific (different IDs per instance).
        /// Null when unconfigured, for the same reason as <see cref="GroupId"/>.
        /// </summary>
        public long? BrandId { get; set; }


    }
}
