using System;
using System.Collections.Generic;
using System.Text;
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class TicketFieldsResponseDto
    {
        public List<CustomFieldMetaDataDto> TicketFields { get; set; }
    }
}
