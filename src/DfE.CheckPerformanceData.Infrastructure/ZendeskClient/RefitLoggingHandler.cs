namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    public class RefitLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read the request body as a string if it exists, otherwise use an empty string
            var requestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync()
                : string.Empty;

            // Log the outgoing request details
            Console.WriteLine("➡️ REQUEST");
            Console.WriteLine($"{request.Method} {request.RequestUri}");
            Console.WriteLine(request.Headers);
            Console.WriteLine(requestBody);

            // Send the request and await the response
            var response = await base.SendAsync(request, cancellationToken);

            // Read the response body as a string
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log the incoming response details
            Console.WriteLine("⬅️ RESPONSE");
            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine(response.Headers);
            Console.WriteLine(responseBody);

            return response;
        }
    }
}
