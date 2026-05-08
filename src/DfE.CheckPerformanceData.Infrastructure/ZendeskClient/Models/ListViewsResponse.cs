using System.Collections.Generic;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models
{
    public class ListViewsResponse : BasePagedModelResponse
    {
        public List<View> Views { get; init; } = new();
    }
}
