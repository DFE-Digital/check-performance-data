using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class ListViewTicketsRequestDto
    {
        public int? Page { get; set; }
        public int? PerPage { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
    }
}
