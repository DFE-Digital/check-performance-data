using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Self-contained (no deployment, no browser): a loopback listener plays the part of a
// review app whose ingress closes a kept-alive connection just as a seed POST arrives.
public class TestHttpClientsTests
{
    [Fact]
    public async Task SendAsync_ResendsAPost_WhenTheServerClosesTheConnectionWithoutAnswering()
    {
        using var server = new DroppingServer(connectionsToDrop: 1);

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("title", "A page") }),
        };
        request.Headers.Add("Cookie", "antiforgery=abc");

        using var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(2, server.Requests.Count);
        // The resent request is the same request: body and cookie arrive intact, once each.
        Assert.EndsWith("title=A+page", server.Requests[1]);
        Assert.Single(server.Requests[1].Split("\r\n"), line => line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("antiforgery=abc", server.Requests[1]);
    }

    [Fact]
    public async Task SendAsync_GivesUp_WhenTheServerKeepsClosingTheConnection()
    {
        using var server = new DroppingServer(connectionsToDrop: int.MaxValue);

        using var request = new HttpRequestMessage(HttpMethod.Post, server.Url)
        {
            Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("title", "A page") }),
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => TestHttpClients.SendAsync(request));
        Assert.Equal(TestHttpClients.MaxSendAttempts, server.Requests.Count);
    }

    // Reads each request in full, then closes the first N connections without a response and
    // answers the rest with a 302, closing after each so every request is a fresh connection.
    private sealed class DroppingServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private int _toDrop;

        public DroppingServer(int connectionsToDrop)
        {
            _toDrop = connectionsToDrop;
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/admin/pages/create";
            _ = AcceptLoopAsync();
        }

        public string Url { get; }
        public List<string> Requests { get; } = [];

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    Requests.Add(await ReadRequestAsync(stream));
                    if (_toDrop > 0)
                    {
                        _toDrop--;
                        continue;
                    }
                    var reply = "HTTP/1.1 302 Found\r\nLocation: /admin/pages/x/edit\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var text = new StringBuilder();
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                text.Append(Encoding.ASCII.GetString(buffer, 0, read));
                var all = text.ToString();
                var headerEnd = all.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0) continue;
                var lengthLine = all.Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                var length = lengthLine is null ? 0 : int.Parse(lengthLine.Split(':')[1].Trim());
                if (all.Length - (headerEnd + 4) >= length) break;
            }
            return text.ToString();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
