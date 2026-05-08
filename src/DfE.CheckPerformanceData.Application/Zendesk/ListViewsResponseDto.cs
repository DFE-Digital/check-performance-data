using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class ListViewsResponseDto : BasePagedModelResponseDto
    {
        public List<ViewDto> Views { get; set; } = new();
    }
}
