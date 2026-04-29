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
    }
    public class PollySettings
    {
        public const string SectionName = "PollySettings";
        public int MaxRetryAttempts { get; set; } = 3;
        public int BaseDelayMilliseconds { get; set; } = 1000;
        public int JitterMilliseconds { get; set; } = 500;
    }
}
