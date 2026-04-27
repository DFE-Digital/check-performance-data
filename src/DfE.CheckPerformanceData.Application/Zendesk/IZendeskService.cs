

namespace DfE.CheckPerformanceData.Application.ZendeskClient
{
    public interface IZendeskService
    {
        Task<ListViewsResponseDto> ListViewsAsync(int? pageSize);

        Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null);

        Task<TicketFieldsResponseDto> GetTicketFields();

        Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null);

        Task<GetTicketResponseDto> GetTicketAsync(long ticketId);


        Task<TicketCommentsResponseDto> GetTicketComments(long ticketId);
        Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId);
    }
}