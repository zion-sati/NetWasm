using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NetWasmService;

// The host calls Handle for each request. HttpClient and the other .NET APIs
// work here as they do in a command.
public static partial class ServiceExports
{
    public static async partial Task<HttpResponseMessage> Handle(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"Hello from C#: {request.Method} {request.RequestUri!.PathAndQuery} ({body.Length} bytes)\n"),
        };
    }
}
