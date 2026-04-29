using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class ListViewTicketsResponse : BasePagedModelResponse
    {
        public List<Ticket> Tickets { get; init; } = new();
    }
}
