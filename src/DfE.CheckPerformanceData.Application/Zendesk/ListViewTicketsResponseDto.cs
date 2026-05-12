using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class ListViewTicketsResponseDto : BasePagedModelResponseDto
    {
        public List<TicketDto> Tickets { get; set; } = new();
    }
}
