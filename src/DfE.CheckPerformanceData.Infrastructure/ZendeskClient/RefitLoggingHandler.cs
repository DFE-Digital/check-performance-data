using System;
using System.Collections.Generic;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient // this could be moved to a common or refit folder/namespace
{
    public class RefitLoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestBody = request.Content != null
                ? await request.Content.ReadAsStringAsync()
                : "";

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
