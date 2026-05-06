using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public class ZendeskSettings
    {
        public const string SectionName = "ZendeskSettings";
        public required string Subdomain { get; set; }
        public required string Domain { get; set; }
        public required string Email { get; set; }
        public required string ApiToken { get; set; }

        /// <summary>
        /// Validates that credentials are configured with actual values (not placeholders).
        /// Call this during startup to fail fast if settings aren't properly set up.
        /// </summary>
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

            if (invalidValues.Count > 0)
            {
                Console.WriteLine($"[WARN] ZendeskSettings: '{string.Join(", ", invalidValues)}' is not configured. " +
                    $"Check user secrets or environment variables.");
            }
        }
    }
    public class PollySettings
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
    public class SchoolCheckingExerciseSettings
    {
        public const string SectionName = "SchoolCheckingExercise";
        public required string TargetViewTitle { get; set; }
        public long GroupId { get; set; }
        public long BrandId { get; set; }
    }
}
