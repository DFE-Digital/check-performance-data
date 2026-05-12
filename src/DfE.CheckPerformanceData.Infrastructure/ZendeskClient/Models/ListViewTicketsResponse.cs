using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public sealed class ListViewTicketsResponse : BasePagedModelResponse
    {
        public List<Ticket> Tickets { get; init; } = new();
    }
}
