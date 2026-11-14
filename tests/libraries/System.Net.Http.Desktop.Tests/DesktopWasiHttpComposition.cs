using System.Net;

namespace System.Net.Http.WasiHttp;

// The desktop stand-in for HttpClientHandler's default transport: these tests compile the package
// sources without the generated wasi:http bindings, which only a WASI host can run.
internal static class WasiHttpTransportComposition
{
    internal static IHttpTransport Create() => new DesktopTransport();

    private sealed class DesktopTransport : IHttpTransport
    {
        public Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
