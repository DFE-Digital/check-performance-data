using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class ListViewsResponseDto : BasePagedModelResponseDto
    {

        public List<ViewDto> Views { get; set; }

    }

}
