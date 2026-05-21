using System;
using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public sealed class TicketFieldsResponseDto
    {
        public List<CustomFieldMetaDataDto> TicketFields { get; set; } = new();
    }
}
