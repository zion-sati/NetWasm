using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.WebAssembly;
using System.Threading;
using System.Threading.Tasks;

namespace NetWasm.Fixtures.ComponentModel;

public static class WasiHttpQualificationComponent
{
    private const ulong Contract = 0x3ff;
    private static Task<ulong>? _pending;
    private static CancellationTokenSource? _cancellation;
    private static int _headerDelayCount;
    private static int _chunkCount;
    private static int _errorCount;
    private static int _headerRejectionCount;
    private static int _disposeCount;

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-start")]
    public static bool QualificationStart(nuint endpointAddress, nuint endpointLength)
    {
        if (_pending is { IsCompleted: false })
        {
            throw new InvalidOperationException();
        }

        ResetCounters();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _pending = ExecuteAsync(
            CanonicalAbi.LiftString(endpointAddress, endpointLength),
            cancellation);
        return !_pending.IsCompleted;
    }

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-is-completed")]
    public static bool QualificationIsCompleted() => _pending?.IsCompleted ?? false;

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-result")]
    public static ulong QualificationResult()
    {
        if (_pending is not { IsCompleted: true })
        {
            throw new InvalidOperationException();
        }

        return _pending.Result;
    }

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-cancel")]
    public static bool QualificationCancel()
    {
        _cancellation?.Cancel();
        return true;
    }

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-dispose")]
    public static bool QualificationDispose()
    {
        try
        {
            using var client = new HttpClient();
            client.Dispose();
            _ = client.GetAsync(new Uri("http://localhost/disposed"));
            return false;
        }
        catch (ObjectDisposedException)
        {
            _disposeCount++;
            return true;
        }
    }

    [WitExport("netwasm:http-spike@1.0.0/acceptance", "qualification-counters")]
    public static ulong QualificationCounters() =>
        unchecked((ulong)(uint)_headerDelayCount) |
        (unchecked((ulong)(uint)_chunkCount) << 8) |
        (unchecked((ulong)(uint)_errorCount) << 16) |
        (unchecked((ulong)(uint)_headerRejectionCount) << 24) |
        (unchecked((ulong)(uint)_disposeCount) << 32);

    public static int Run(int value) => checked(value + (int)Contract);

    private static async Task<ulong> ExecuteAsync(
        string endpoint,
        CancellationTokenSource cancellation)
    {
        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(endpoint));
            request.Headers.Add("x-qualification", "accepted");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellation.Token).ConfigureAwait(false);
            _headerDelayCount++;
            var body = await response.Content!
                .ReadAsStringAsync(cancellation.Token)
                .ConfigureAwait(false);
            var delayedHeaders = response.Headers.GetValues("x-delayed").GetEnumerator();
            if (response.StatusCode != HttpStatusCode.OK ||
                !delayedHeaders.MoveNext() ||
                (string)delayedHeaders.Current! != "true" ||
                body != "header|chunk-a|chunk-b")
            {
                return 0;
            }

            _chunkCount = 2;

            try
            {
                _ = await client.GetAsync(
                    new Uri(endpoint + "?failure=1"),
                    cancellation.Token).ConfigureAwait(false);
                return 0;
            }
            catch (HttpRequestException)
            {
                _errorCount++;
            }

            using var invalidRequest = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(endpoint));
            invalidRequest.Headers.Add("invalid header", "value");
            try
            {
                _ = await client.SendAsync(
                    invalidRequest,
                    cancellation.Token).ConfigureAwait(false);
                return 0;
            }
            catch (HttpRequestException)
            {
                _headerRejectionCount++;
            }

            return Contract;
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static void ResetCounters()
    {
        _headerDelayCount = 0;
        _chunkCount = 0;
        _errorCount = 0;
        _headerRejectionCount = 0;
        _disposeCount = 0;
    }

}
