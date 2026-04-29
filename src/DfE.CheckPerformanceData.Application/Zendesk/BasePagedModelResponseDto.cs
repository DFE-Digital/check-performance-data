using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class BasePagedModelResponseDto
    {
        public Uri? NextPage { get; set; }
        public Uri? PreviousPage { get; set; }
        public int? Count { get; set; }
    }
}
