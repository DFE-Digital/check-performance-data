using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;

public class ZendeskService : IZendeskService
{
    private readonly IZendeskApi _api;
    private readonly ILogger<ZendeskService> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public ZendeskService(IZendeskApi api, ILogger<ZendeskService> logger)
    {
        _api = api;
        _logger = logger;

        var builder = new ResiliencePipelineBuilder();

        builder.AddRetry(new RetryStrategyOptions
        {
            DelayGenerator = static (args) =>
            {
                var delay = Math.Pow(2, args.AttemptNumber - 1) * 1000;
                var jitter = new Random().Next(0, 500);
                return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(delay + jitter));
            },
            MaxRetryAttempts = 3,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<Exception>(ex => ex.GetType().Namespace?.StartsWith("Refit") == true
                    || ex.GetType().Namespace?.StartsWith("System.Net") == true),
            OnRetry = args =>
            {
                _logger.LogWarning(
                    args.Outcome.Exception,
                    "Zendesk API request failed. Retry attempt {RetryAttempt}",
                    args.AttemptNumber);

                return ValueTask.CompletedTask;
            }
        });

        _resiliencePipeline = builder.Build();
    }

    public async Task<GetTicketResponseDto> GetTicketAsync(long ticketId)
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetTicket(ticketId),
                $"GetTicket({ticketId})");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ticket {TicketId}", ticketId);
            throw new ZendeskApiException(
                $"Failed to retrieve ticket {ticketId}.",
                ex);
        }
    }

    public async Task<TicketCommentsResponseDto> GetTicketComments(long ticketId)
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetTicketComments(ticketId),
                $"GetTicketComments({ticketId})");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving comments for ticket {TicketId}", ticketId);
            throw new ZendeskApiException(
                $"Failed to retrieve comments for ticket {ticketId}.",
                ex);
        }
    }

    public async Task<TicketFieldsResponseDto> GetTicketFields()
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetTicketFields(),
                "GetTicketFields");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving ticket fields");
            throw new ZendeskApiException(
                "Failed to retrieve ticket fields.",
                ex);
        }
    }

    public async Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null)
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetTicketsForView(viewId, query),
                $"GetTicketsForView({viewId})");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tickets for view {ViewId}", viewId);
            throw new ZendeskApiException(
                $"Failed to retrieve tickets for view {viewId}.",
                ex);
        }
    }

    public async Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null)
    {
        try
        {
            var ticketsResponse = await ExecuteWithResilienceAsync(
                () => _api.GetTicketsForView(viewId, query),
                $"GetTicketsViewModel({viewId})");
            var ticketFieldsResponse = await ExecuteWithResilienceAsync(
                () => _api.GetTicketFields(),
                "GetTicketFields");

            return new TicketsViewModel
            {
                TicketsResponse = ZendeskMapper.ToDto(ticketsResponse),
                TicketFieldsResponse = ZendeskMapper.ToDto(ticketFieldsResponse)
            };
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building tickets view model for view {ViewId}", viewId);
            throw new ZendeskApiException(
                $"Failed to build tickets view model for view {viewId}.",
                ex);
        }
    }

    public async Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId)
    {
        try
        {
            var ticketResponse = await ExecuteWithResilienceAsync(
                () => _api.GetTicket(ticketId),
                $"GetTicketViewModel({ticketId})");
            var ticketFieldsResponse = await ExecuteWithResilienceAsync(
                () => _api.GetTicketFields(),
                "GetTicketFields");
            var ticketCommentsResponse = await ExecuteWithResilienceAsync(
                () => _api.GetTicketComments(ticketId),
                $"GetTicketComments({ticketId})");

            var ticketFieldsDto = ZendeskMapper.ToDto(ticketFieldsResponse);

            return new GetTicketViewModel
            {
                Ticket = ZendeskMapper.ToDto(ticketResponse).Ticket,
                UserFields = ticketFieldsDto.TicketFields,
                Comments = ZendeskMapper.ToDto(ticketCommentsResponse).Comments
            };
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building ticket view model for ticket {TicketId}", ticketId);
            throw new ZendeskApiException(
                $"Failed to build ticket view model for ticket {ticketId}.",
                ex);
        }
    }

    public async Task<UserFieldsResponseDto> GetUserFieldsAsync()
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetUserFields(),
                "GetUserFields");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user fields");
            throw new ZendeskApiException(
                "Failed to retrieve user fields.",
                ex);
        }
    }

    public async Task<CreateTicketResponseDto> CreateTicketAsync(CreateTicketRequestDto request)
    {
        try
        {
            var apiRequest = ZendeskMapper.ToEntity(request);

            var response = await ExecuteWithResilienceAsync(
                () => _api.CreateTicket(apiRequest),
                "CreateTicket");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating ticket");
            throw new ZendeskApiException(
                "Failed to create ticket.",
                ex);
        }
    }

    public async Task<ListViewsResponseDto> ListViewsAsync(int? pageSize)
    {
        try
        {
            var response = await ExecuteWithResilienceAsync(
                () => _api.GetViews(pageSize ?? 200),
                "ListViews");

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing views");
            throw new ZendeskApiException(
                "Failed to list views.",
                ex);
        }
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName)
    {
        try
        {
            _logger.LogDebug("Executing Zendesk operation: {OperationName}", operationName);
            var result = await _resiliencePipeline.ExecuteAsync(
                async ct => await action(),
                CancellationToken.None);
            _logger.LogDebug("Successfully completed Zendesk operation: {OperationName}", operationName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resilience pipeline failed for operation: {OperationName}", operationName);
            throw;
        }
    }
}