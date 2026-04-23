using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public class ZendeskSettings
    {
        public const string SectionName = "ZendeskSettings";
        public string Subdomain { get; set; }
        public string Domain { get; set; }
        public string Email { get; set; }
        public string ApiToken { get; set; }
    }
}
