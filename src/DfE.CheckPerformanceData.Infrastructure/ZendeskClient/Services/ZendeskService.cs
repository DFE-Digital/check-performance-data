using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services
{
    public class ZendeskService : IZendeskService
    {
        private readonly IZendeskApi _api;

        public ZendeskService(IZendeskApi api)
        {
            _api = api;
        }

        public Task<GetTicketResponseDto> GetTicketAsync(long ticketId)
        {
            throw new NotImplementedException();
        }

        public Task<TicketCommentsResponseDto> GetTicketComments(long ticketId)
        {
            throw new NotImplementedException();
        }

        public Task<TicketFieldsResponseDto> GetTicketFields()
        {
            throw new NotImplementedException();
        }

        public Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null)
        {
            throw new NotImplementedException();
        }

        public Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null)
        {
            throw new NotImplementedException();
        }

        public Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId)
        {
            throw new NotImplementedException();
        }

        public async Task<ListViewsResponseDto> ListViewsAsync(int? pageSize)
        {
            var views = await _api.GetViews(200);
            return ZendeskMapper.ToDto(views);
        }
    }
}
