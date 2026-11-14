using System;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Net.Requests.Tests;

// The managed request APIs: creator dispatch and registration, request state, proxies, cache policies, exceptions,
// response defaults and WebClient over explicit in-memory requests, which behave alike on both lanes. The netwasm lane
// also checks the capability boundaries, which never reach the network.
public sealed class RequestsTests
{
    [Test]
    public async Task CreateDispatchesByPrefixWithLongestPrefixPrecedence()
    {
        await Assert.That(WebRequest.Create("http://example.test/") is HttpWebRequest).IsTrue();
        await Assert.That(WebRequest.Create("ftp://example.test/file") is FtpWebRequest).IsTrue();
        await Assert.That(WebRequest.Create("file:///data/file.txt") is FileWebRequest).IsTrue();

        var scheme = new RecordingCreator("scheme");
        var path = new RecordingCreator("path");
        await Assert.That(WebRequest.RegisterPrefix("netwasmtest:", scheme)).IsTrue();
        await Assert.That(WebRequest.RegisterPrefix("NETWASMTEST:", scheme)).IsFalse();
        await Assert.That(WebRequest.RegisterPrefix("netwasmtest://host/deep/", path)).IsTrue();

        await Assert.That(((InMemoryRequest)WebRequest.Create("netwasmtest://host/other")).CreatedBy).IsEqualTo("scheme");
        await Assert.That(((InMemoryRequest)WebRequest.Create("netwasmtest://host/deep/item")).CreatedBy).IsEqualTo("path");
    }

    [Test]
    public async Task HttpWebRequestKeepsManagedStateAndHeaders()
    {
        var request = WebRequest.CreateHttp("http://example.test/resource");

        await Assert.That(request.Method).IsEqualTo("GET");
        await Assert.That(request.AllowAutoRedirect).IsTrue();
        await Assert.That(request.MaximumAutomaticRedirections).IsEqualTo(50);
        await Assert.That(request.Timeout).IsEqualTo(100000);
        await Assert.That(request.ReadWriteTimeout).IsEqualTo(300000);
        await Assert.That(request.KeepAlive).IsTrue();
        await Assert.That(request.ProtocolVersion).IsEqualTo(HttpVersion.Version11);
        // As upstream: without a Content-Length header the getter's TryParse leaves 0.
        await Assert.That(request.ContentLength).IsEqualTo(0L);

        request.AddRange(10, 20);
        request.AddRange(5);
        await Assert.That(request.Headers["Range"]).IsEqualTo("bytes=10-20,5-");
        await Assert.That(Throws<InvalidOperationException>(() => request.AddRange("items", 1, 2))).IsTrue();
        await Assert.That(Throws<ArgumentException>(() => request.Method = "BAD METHOD")).IsTrue();

        request.Accept = "text/plain";
        request.UserAgent = "netwasm-test";
        await Assert.That(request.Headers["Accept"]).IsEqualTo("text/plain");
        await Assert.That(request.Headers["User-Agent"]).IsEqualTo("netwasm-test");

        ServicePoint servicePoint = request.ServicePoint;
        await Assert.That(ReferenceEquals(servicePoint, request.ServicePoint)).IsTrue();
        await Assert.That(servicePoint.ConnectionName).IsEqualTo("http");
    }

    [Test]
    public async Task WebProxyMatchesBypassPatternsAndLocalHosts()
    {
        var proxy = new WebProxy("http://proxy.test:8080", BypassOnLocal: true, new[] { "contoso\\.test" });

        await Assert.That(proxy.IsBypassed(new Uri("http://www.contoso.test/path"))).IsTrue();
        await Assert.That(proxy.IsBypassed(new Uri("http://localhost/"))).IsTrue();
        await Assert.That(proxy.IsBypassed(new Uri("http://other.test/"))).IsFalse();
        await Assert.That(proxy.GetProxy(new Uri("http://other.test/"))!.ToString()).IsEqualTo("http://proxy.test:8080/");
        await Assert.That(proxy.GetProxy(new Uri("http://www.contoso.test/"))!.ToString()).IsEqualTo("http://www.contoso.test/");
        await Assert.That(proxy.BypassList.Length).IsEqualTo(1);

        var direct = new WebProxy();
        await Assert.That(direct.IsBypassed(new Uri("http://other.test/"))).IsTrue();

        IWebProxy empty = GlobalProxySelection.GetEmptyWebProxy();
        var destination = new Uri("http://other.test/");
        await Assert.That(empty.IsBypassed(destination)).IsTrue();
        await Assert.That(ReferenceEquals(empty.GetProxy(destination), destination)).IsTrue();
    }

    [Test]
    public async Task CachePoliciesAndExceptionsKeepTheirValues()
    {
        var reload = new HttpRequestCachePolicy(HttpRequestCacheLevel.Reload);
        await Assert.That(reload.Level).IsEqualTo(HttpRequestCacheLevel.Reload);

        var maxAge = new HttpRequestCachePolicy(HttpCacheAgeControl.MaxAge, TimeSpan.FromSeconds(10));
        await Assert.That(maxAge.Level).IsEqualTo(HttpRequestCacheLevel.Default);
        await Assert.That(maxAge.MaxAge).IsEqualTo(TimeSpan.FromSeconds(10));

        var unknown = new WebException("failed");
        await Assert.That(unknown.Status).IsEqualTo(WebExceptionStatus.UnknownError);
        await Assert.That(unknown.Response).IsNull();
        var inner = new IOException("inner");
        var timeout = new WebException("timed out", inner, WebExceptionStatus.Timeout, null);
        await Assert.That(timeout.Status).IsEqualTo(WebExceptionStatus.Timeout);
        await Assert.That(ReferenceEquals(timeout.InnerException, inner)).IsTrue();
        await Assert.That(new ProtocolViolationException("bad") is InvalidOperationException).IsTrue();
    }

    [Test]
    public async Task WebResponseBaseDefaultsAndLifecycle()
    {
        var response = new MinimalResponse();

        await Assert.That(response.IsFromCache).IsFalse();
        await Assert.That(response.IsMutuallyAuthenticated).IsFalse();
        await Assert.That(response.SupportsHeaders).IsFalse();
        await Assert.That(Throws<NotImplementedException>(() => _ = response.ContentLength)).IsTrue();
        await Assert.That(Throws<NotImplementedException>(() => response.GetResponseStream())).IsTrue();

        response.Dispose();
        await Assert.That(response.CloseCount).IsEqualTo(1);
    }

    [Test]
    public async Task WebClientResolvesAddressesAndDecodesInMemoryResponses()
    {
        using var client = new InMemoryWebClient("hello");
        client.BaseAddress = "http://example.test/base/";
        client.QueryString.Add("q", "1");

        await Assert.That(client.DownloadString("item")).IsEqualTo("hello");
        await Assert.That(client.LastAddress!.ToString()).IsEqualTo("http://example.test/base/item?q=1");
        await Assert.That(Encoding.UTF8.GetString(client.DownloadData("item"))).IsEqualTo("hello");
        await Assert.That(await client.DownloadStringTaskAsync(new Uri("http://example.test/async"))).IsEqualTo("hello");
        await Assert.That(client.IsBusy).IsFalse();
    }

    [Test]
    public async Task FtpRequestConfigurationValidatesAndAbortRecordsCancellation()
    {
        var ftp = (FtpWebRequest)WebRequest.Create("ftp://example.test/file");

        await Assert.That(ftp.Method).IsEqualTo(WebRequestMethods.Ftp.DownloadFile);
        await Assert.That(ftp.UsePassive).IsTrue();
        await Assert.That(ftp.UseBinary).IsTrue();
        await Assert.That(ftp.Timeout).IsEqualTo(100000);
        ftp.Method = WebRequestMethods.Ftp.ListDirectory;
        await Assert.That(ftp.Method).IsEqualTo("NLST");
        await Assert.That(Throws<ArgumentException>(() => ftp.Method = "BOGUS")).IsTrue();
        await Assert.That(Throws<ArgumentOutOfRangeException>(() => ftp.ContentOffset = -1)).IsTrue();

        ftp.Abort();
        // As upstream: Abort records ExceptionHelper.RequestAbortedException, a WebException without a status.
        await Assert.That(ThrowsWebException(() => ftp.GetResponse(), WebExceptionStatus.UnknownError)).IsTrue();

        var file = (FileWebRequest)WebRequest.Create("file:///data/file.txt");
        file.Abort();
        await Assert.That(ThrowsWebException(() => file.GetResponseAsync(), WebExceptionStatus.RequestCanceled)).IsTrue();
    }

    // FTP reaches the platform's sockets; a port nothing listens on refuses the connection.
    [Test]
    public async Task FtpToAPortNothingListensOnFails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var request = (FtpWebRequest)WebRequest.Create($"ftp://127.0.0.1:{port}/file.txt");
        request.Method = WebRequestMethods.Ftp.DownloadFile;
        WebExceptionStatus? status = null;
        try
        {
            using var response = await request.GetResponseAsync();
        }
        catch (WebException e)
        {
            status = e.Status;
        }

        await Assert.That(status).IsEqualTo(WebExceptionStatus.ConnectFailure);
    }

    // HttpWebRequest.GetResponse blocks on the platform's HTTP, as on .NET; a port nothing listens on refuses the
    // connection, which upstream reports as UnknownError (only name resolution has its own status).
    [Test]
    public async Task HttpGetResponseToAPortNothingListensOnFails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var request = WebRequest.CreateHttp($"http://127.0.0.1:{port}/");
        await Assert.That(ThrowsWebException(() => request.GetResponse().Dispose(), WebExceptionStatus.UnknownError)).IsTrue();
    }

#if NETWASM
    [Test]
    public async Task UnavailableCapabilitiesThrowPlatformNotSupportedBeforeSending()
    {
        // The system proxy is upstream's WASI one: the host routes every request.
        var request = WebRequest.CreateHttp("http://example.test/");
        var system = WebRequest.GetSystemWebProxy();
        await Assert.That(ReferenceEquals(system, WebRequest.GetSystemWebProxy())).IsTrue();
        await Assert.That(system.IsBypassed(new Uri("http://example.test/"))).IsTrue();
        await Assert.That(system.GetProxy(new Uri("http://example.test/"))).IsNull();
        await Assert.That(ReferenceEquals(request.Proxy, system)).IsTrue();

        // Settings the WASI transport cannot honor fail before anything is sent.
        var withCredentials = WebRequest.CreateHttp("http://example.test/");
        withCredentials.Credentials = new NetworkCredential("user", "secret");
        await Assert.That(await ThrowsAsync<PlatformNotSupportedException>(() => withCredentials.GetResponseAsync())).IsTrue();

        var withProxy = WebRequest.CreateHttp("http://example.test/");
        withProxy.Proxy = new WebProxy("http://proxy.test:8080");
        await Assert.That(await ThrowsAsync<PlatformNotSupportedException>(() => withProxy.GetResponseAsync())).IsTrue();
    }
#endif

    // WebClient downloads to and uploads from files by path, as on .NET.
    [Test]
    public async Task WebClientDownloadsToAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-webclient-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            using var client = new InMemoryWebClient("hello");
            await client.DownloadFileTaskAsync(new Uri("http://example.test/"), path);

            await Assert.That(File.ReadAllText(path)).IsEqualTo("hello");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // FileWebRequest reads and writes files, as on .NET (0.12's file system; test hosts grant a private /tmp).
    [Test]
    public async Task FileRequestsWriteAndReadFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-file-request-" + Guid.NewGuid().ToString("N") + ".bin");
        var uri = new Uri(path);
        try
        {
            var upload = (FileWebRequest)WebRequest.Create(uri);
            upload.Method = WebRequestMethods.File.UploadFile;
            using (var stream = await upload.GetRequestStreamAsync())
            {
                await stream.WriteAsync(new byte[] { 1, 2, 3 });
            }
            using (await upload.GetResponseAsync())
            {
            }

            using var response = (FileWebResponse)await WebRequest.Create(uri).GetResponseAsync();
            var buffer = new byte[8];
            int read;
            using (var body = response.GetResponseStream())
            {
                read = await body.ReadAsync(buffer);
            }

            await Assert.That(read).IsEqualTo(3);
            await Assert.That(buffer[2]).IsEqualTo((byte)3);
            await Assert.That(response.ContentLength).IsEqualTo(3L);
            await Assert.That(response.ContentType).IsEqualTo("application/octet-stream");
            await Assert.That(response.Headers["Content-Length"]).IsEqualTo("3");
            await Assert.That(response.ResponseUri).IsEqualTo(uri);
            response.Close();
            await Assert.That(Throws<ObjectDisposedException>(() => _ = response.ContentLength)).IsTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool Throws<TException>(Action operation) where TException : Exception
    {
        try
        {
            operation();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsAsync<TException>(Func<Task> operation) where TException : Exception
    {
        try
        {
            await operation();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static bool ThrowsWebException(Action operation, WebExceptionStatus status)
    {
        try
        {
            operation();
            return false;
        }
        catch (WebException e)
        {
            return e.Status == status;
        }
    }

    private sealed class RecordingCreator(string name) : IWebRequestCreate
    {
        public WebRequest Create(Uri uri) => new InMemoryRequest(uri, Array.Empty<byte>(), name);
    }

    // A request whose response is supplied in memory, synchronously or through Begin/End.
    private sealed class InMemoryRequest(Uri uri, byte[] body, string createdBy) : WebRequest
    {
        public string CreatedBy { get; } = createdBy;

        public override Uri RequestUri { get; } = uri;

        public override WebResponse GetResponse() => new InMemoryResponse(body);

        public override IAsyncResult BeginGetResponse(AsyncCallback? callback, object? state) =>
            TaskToAsyncResult.Begin(Task.FromResult<WebResponse>(new InMemoryResponse(body)), callback, state);

        public override WebResponse EndGetResponse(IAsyncResult asyncResult) =>
            TaskToAsyncResult.End<WebResponse>(asyncResult);
    }

    private sealed class InMemoryResponse(byte[] body) : WebResponse
    {
        public override long ContentLength => body.Length;

        public override Stream GetResponseStream() => new MemoryStream(body, writable: false);
    }

    private sealed class MinimalResponse : WebResponse
    {
        public int CloseCount { get; private set; }

        public override void Close() => CloseCount++;
    }

    private sealed class InMemoryWebClient(string body) : WebClient
    {
        public Uri? LastAddress { get; private set; }

        protected override WebRequest GetWebRequest(Uri address)
        {
            LastAddress = address;
            return new InMemoryRequest(address, Encoding.UTF8.GetBytes(body), "client");
        }
    }
}
