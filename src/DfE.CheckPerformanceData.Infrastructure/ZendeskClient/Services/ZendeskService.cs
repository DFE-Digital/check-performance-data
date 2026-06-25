using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;

public sealed class ZendeskService : IZendeskService
{
    private readonly IZendeskApi _api;
    private readonly PollySettings _settings;
    private readonly ILogger<ZendeskService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public ZendeskService(IZendeskApi api, IOptions<PollySettings> settings, ILogger<ZendeskService> logger)
    {
        _api = api;
        _settings = settings.Value;
        _logger = logger;

        _resiliencePipeline = ResiliencePipelineFactory.CreateRetryPipeline(_settings, _logger);
    }

    public async Task<ListViewsResponseDto> ListViewsAsync(int? pageSize)
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetViews(pageSize ?? 20),
            ZendeskMapper.ToDto,
            "List views",
            ex => new ZendeskApiException("Failed to list views.", ex));
    }

    public async Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null)
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetTicketsForView(viewId, query),
            ZendeskMapper.ToDto,
            $"Retrieve tickets for view {viewId}",
            ex => new ZendeskApiException($"Failed to retrieve tickets for view {viewId}.", ex));
    }

    public async Task<TicketFieldsResponseDto> GetTicketFields()
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetTicketFields(),
            ZendeskMapper.ToDto,
            "Retrieve ticket fields",
            ex => new ZendeskApiException("Failed to retrieve ticket fields.", ex));
    }

    public async Task<UserFieldsResponseDto> GetUserFieldsAsync()
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetUserFields(),
            ZendeskMapper.ToDto,
            "Retrieve user fields",
            ex => new ZendeskApiException("Failed to retrieve user fields.", ex));
    }

    public async Task<CreateTicketResponseDto> CreateTicketAsync(CreateTicketRequestDto request)
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.CreateTicket(ZendeskMapper.ToEntity(request)),
            ZendeskMapper.ToDto,
            "Create ticket",
            ex => new ZendeskApiException("Failed to create ticket.", ex));
    }

    public async Task<GetTicketResponseDto> GetTicketAsync(long ticketId)
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetTicket(ticketId),
            ZendeskMapper.ToDto,
            $"Retrieve ticket {ticketId}",
            ex => new ZendeskApiException($"Failed to retrieve ticket {ticketId}.", ex));
    }

    public async Task<TicketCommentsResponseDto> GetTicketComments(long ticketId)
    {
        return await ExecuteWithResilienceAndMappingAsync(
            () => _api.GetTicketComments(ticketId),
            ZendeskMapper.ToDto,
            $"Error retrieving comments for ticket {ticketId}",
            ex => new ZendeskApiException($"Failed to retrieve comments for ticket {ticketId}.", ex));
    }

    public async Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null)
    {
        try
        {
            var ticketsTask = ExecuteWithResilienceAsync(
                () => _api.GetTicketsForView(viewId, query),
                $"GetTicketsViewModel({viewId})");
            var ticketFieldsTask = ExecuteWithResilienceAsync(
                () => _api.GetTicketFields(),
                "GetTicketFields");

            await Task.WhenAll(ticketsTask, ticketFieldsTask);

            return new TicketsViewModel
            {
                TicketsResponse = ZendeskMapper.ToDto(await ticketsTask),
                TicketFieldsResponse = ZendeskMapper.ToDto(await ticketFieldsTask)
            };
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building tickets view model for view {ViewId}", viewId);
            throw new ZendeskApiException($"Failed to build tickets view model for view {viewId}.", ex.InnerException ?? ex);
        }
    }

    public async Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId)
    {
        try
        {
            var ticketTask = ExecuteWithResilienceAsync(
                () => _api.GetTicket(ticketId),
                $"GetTicketViewModel({ticketId})");
            var ticketFieldsTask = ExecuteWithResilienceAsync(
                () => _api.GetTicketFields(),
                "GetTicketFields");
            var ticketCommentsTask = ExecuteWithResilienceAsync(
                () => _api.GetTicketComments(ticketId),
                $"GetTicketComments({ticketId})");

            await Task.WhenAll(ticketTask, ticketFieldsTask, ticketCommentsTask);

            var ticketFieldsDto = ZendeskMapper.ToDto(await ticketFieldsTask);

            return new GetTicketViewModel
            {
                Ticket = ZendeskMapper.ToDto(await ticketTask).Ticket,
                UserFields = ticketFieldsDto.TicketFields,
                Comments = ZendeskMapper.ToDto(await ticketCommentsTask).Comments ?? new()                
            };
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building ticket view model for ticket {TicketId}", ticketId);
            throw new ZendeskApiException($"Failed to build ticket view model for ticket {ticketId}.", ex.InnerException ?? ex);
        }
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName)
    {
        return await ResiliencePipelineHelper.ExecuteWithResilienceAsync(
            _resiliencePipeline,
            () => action(),
            operationName,
            logger: _logger,
            onSuccess: () => _logger.LogDebug("Successfully completed Zendesk operation: {OperationName}", operationName));
    }

    private async Task<TDto> ExecuteWithResilienceAndMappingAsync<TIn, TDto>(
        Func<Task<TIn>> action,
        Func<TIn, TDto> mapper,
        string operationName,
        Func<Exception, ZendeskApiException> errorFactory)
    {
        return await ResiliencePipelineHelper.ExecuteWithResilienceAndMapAsync(
            _resiliencePipeline,
            () => action(),
            mapper,
            operationName,
            logger: _logger,
            onSuccess: () => _logger.LogDebug("Successfully completed Zendesk operation: {OperationName}", operationName),
            errorFactory: errorFactory);
    }
}
