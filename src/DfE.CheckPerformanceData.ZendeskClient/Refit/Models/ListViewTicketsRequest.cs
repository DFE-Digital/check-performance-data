using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.ZendeskClient.Refit.Models
{
    public class ListViewTicketsRequest
    {
        public int? Page { get; set; }
        public int? PerPage { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
    }
}
