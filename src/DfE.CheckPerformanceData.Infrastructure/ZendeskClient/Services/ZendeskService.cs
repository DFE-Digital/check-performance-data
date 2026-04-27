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

        public async Task<GetTicketResponseDto> GetTicketAsync(long ticketId)
        {
            var response = await _api.GetTicket(ticketId);
            return ZendeskMapper.ToDto(response);
        }

        public async Task<TicketCommentsResponseDto> GetTicketComments(long ticketId)
        {
            var response = await _api.GetTicketComments(ticketId);
            return ZendeskMapper.ToDto(response);
        }

        public async Task<TicketFieldsResponseDto> GetTicketFields()
        {
            var response = await _api.GetTicketFields();
            return ZendeskMapper.ToDto(response);
        }

        public async Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null)
        {
            var response = await _api.GetTicketsForView(viewId, query);
            return ZendeskMapper.ToDto(response);
        }

        public async Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null)
        {
            var ticketsResponse = await _api.GetTicketsForView(viewId, query);
            var ticketFieldsResponse = await _api.GetTicketFields();

            return new TicketsViewModel
            {
                TicketsResponse = ZendeskMapper.ToDto(ticketsResponse),
                TicketFieldsResponse = ZendeskMapper.ToDto(ticketFieldsResponse)
            };
        }

        public async Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId)
        {
            var ticketResponse = await _api.GetTicket(ticketId);
            var ticketFieldsResponse = await _api.GetTicketFields();
            var ticketCommentsResponse = await _api.GetTicketComments(ticketId);

            var ticketFieldsDto = ZendeskMapper.ToDto(ticketFieldsResponse);

            return new GetTicketViewModel
            {
                Ticket = ZendeskMapper.ToDto(ticketResponse).Ticket,
                UserFields = ticketFieldsDto.TicketFields,
                Comments = ZendeskMapper.ToDto(ticketCommentsResponse).Comments
            };
        }

        public async Task<UserFieldsResponseDto> GetUserFieldsAsync()
        {
            var response = await _api.GetUserFields();
            return ZendeskMapper.ToDto(response);
        }

        public async Task<CreateTicketResponseDto> CreateTicketAsync(CreateTicketRequestDto request)
        {
            var apiRequest = ZendeskMapper.ToEntity(request);
            var response = await _api.CreateTicket(apiRequest);
            return ZendeskMapper.ToDto(response);
        }

        public async Task<ListViewsResponseDto> ListViewsAsync(int? pageSize)
        {
            var views = await _api.GetViews(200);
            return ZendeskMapper.ToDto(views);
        }
    }
}
