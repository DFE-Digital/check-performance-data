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
}
