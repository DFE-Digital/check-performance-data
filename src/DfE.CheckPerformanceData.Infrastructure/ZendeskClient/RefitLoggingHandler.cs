namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    using System.Net.Http;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public sealed class RefitLoggingHandler(ILogger<RefitLoggingHandler> logger) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = await ReadBody(request.Content);
            logger.LogDebug("Zendesk API request: {Method} {Uri}", request.Method, request.RequestUri);

            var response = await base.SendAsync(request, cancellationToken);

            var responseBody = await ReadBody(response.Content);
            logger.LogDebug("Zendesk API response: {StatusCode}", response.StatusCode);

            return response;
        }

        private static async Task<string> ReadBody(HttpContent? content)
        {
            if (content is null)
                return string.Empty;

            try
            {
                return await content.ReadAsStringAsync();
            }
            catch
            {
                return "(unable to read body)";
            }
        }
    }
}
