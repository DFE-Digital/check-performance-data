using System;
namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public class ViewDto
    {
        public long Id { get; set; }

        public string Title { get; set; }

        public bool Active { get; set; }

        public int Position { get; set; }
    }
}
