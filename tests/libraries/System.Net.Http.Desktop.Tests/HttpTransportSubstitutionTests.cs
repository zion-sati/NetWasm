using System.Net;
using System.Net.Http;
using System.Text;

namespace NetWasm.SystemNetHttp.Tests;

public sealed class HttpTransportSubstitutionTests
{
    [Fact]
    public async Task PublicClientContractSurvivesTransportStrategySubstitution()
    {
        var transports = new IHttpTransport[]
        {
            new DeterministicTransport(),
            new AlternateTransport(),
        };

        foreach (var transport in transports)
        {
            using var client = new HttpClient(new HttpClientHandler(transport));
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                new Uri("https://example.test/substitution"))
            {
                Content = new StringContent("payload", Encoding.UTF8, "text/plain"),
            };
            request.Headers.Add("x-transport-contract", "stable");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("accepted", await response.Content!.ReadAsStringAsync());
            Assert.Equal(1, ((ISubstitutionProbe)transport).InvocationCount);
            Assert.Equal("stable", ((ISubstitutionProbe)transport).ObservedHeader);
            Assert.Equal("payload", ((ISubstitutionProbe)transport).ObservedBody);
        }
    }

    private interface ISubstitutionProbe
    {
        int InvocationCount { get; }
        string? ObservedHeader { get; }
        string? ObservedBody { get; }
    }

    private sealed class DeterministicTransport : IHttpTransport, ISubstitutionProbe
    {
        public int InvocationCount { get; private set; }
        public string? ObservedHeader { get; private set; }
        public string? ObservedBody { get; private set; }

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ObservedHeader = request.Headers.GetValues("x-transport-contract").Single();
            ObservedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return AcceptedResponse();
        }
    }

    private sealed class AlternateTransport : IHttpTransport, ISubstitutionProbe
    {
        public int InvocationCount { get; private set; }
        public string? ObservedHeader { get; private set; }
        public string? ObservedBody { get; private set; }

        public async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ObservedHeader = request.Headers.GetValues("x-transport-contract").Single();
            ObservedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return AcceptedResponse();
        }
    }

    private static HttpResponseMessage AcceptedResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent("accepted", Encoding.UTF8, "text/plain"),
        };
}
