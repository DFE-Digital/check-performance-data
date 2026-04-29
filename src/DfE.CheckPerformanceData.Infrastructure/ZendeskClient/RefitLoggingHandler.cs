using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient
{
    public class RefitLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync()
                : string.Empty;

            Console.WriteLine("➡️ REQUEST");
            Console.WriteLine($"{request.Method} {request.RequestUri}");
            Console.WriteLine(request.Headers);
            Console.WriteLine(requestBody);

            var response = await base.SendAsync(request, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine("⬅️ RESPONSE");
            Console.WriteLine($"Status: {response.StatusCode}");
            Console.WriteLine(response.Headers);
            Console.WriteLine(responseBody);

            return response;
        }
    }
}
