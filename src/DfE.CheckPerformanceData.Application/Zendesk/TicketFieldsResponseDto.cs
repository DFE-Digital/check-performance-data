using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketFieldsResponseDto
    {
        public List<CustomFieldMetaDataDto> TicketFields { get; set; } = new();
    }
}
