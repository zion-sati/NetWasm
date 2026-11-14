// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) X509ResourceClient without its HTTP client. Upstream reaches
// SocketsHttpHandler and HttpClient through type names (UnsafeAccessorType and reflection), which NetWasm does not
// support, and System.Net.Http references this assembly rather than the reverse. So there is no client, as upstream
// behaves when System.Net.Http cannot be loaded: every AIA, CRL and OCSP download reports it and returns null, and
// chain building works offline (custom trust, or certificates and CRLs the program supplies).

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal static partial class X509ResourceClient
    {
        private static readonly Func<string, CancellationToken, bool, Task<byte[]?>>? s_downloadBytes = null;

        static partial void ReportNoClient();
        static partial void ReportNegativeTimeout();
        static partial void ReportDownloadStart(long totalMillis, string uri);
        static partial void ReportDownloadStop(int bytesDownloaded);
        static partial void ReportRedirectsExceeded();
        static partial void ReportRedirected(Uri newUri);
        static partial void ReportRedirectNotFollowed(Uri redirectUri);

        internal static byte[]? DownloadAsset(string uri, TimeSpan downloadTimeout)
        {
            Task<byte[]?> task = DownloadAssetCore(uri, downloadTimeout, async: false);
            Debug.Assert(task.IsCompletedSuccessfully);
            return task.Result;
        }

        internal static Task<byte[]?> DownloadAssetAsync(string uri, TimeSpan downloadTimeout)
        {
            return DownloadAssetCore(uri, downloadTimeout, async: true);
        }

        private static async Task<byte[]?> DownloadAssetCore(string uri, TimeSpan downloadTimeout, bool async)
        {
            if (s_downloadBytes is null)
            {
                ReportNoClient();

                return null;
            }

            if (downloadTimeout <= TimeSpan.Zero)
            {
                ReportNegativeTimeout();

                return null;
            }

            long totalMillis = (long)downloadTimeout.TotalMilliseconds;

            ReportDownloadStart(totalMillis, uri);

            CancellationTokenSource? cts = totalMillis > int.MaxValue ? null : new CancellationTokenSource((int)totalMillis);
            byte[]? ret = null;

            try
            {
                Task<byte[]?> task = s_downloadBytes(uri, cts?.Token ?? default, async);
                await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (task.IsCompletedSuccessfully)
                {
                    return ret = task.Result;
                }
            }
            catch { }
            finally
            {
                cts?.Dispose();

                ReportDownloadStop(ret?.Length ?? 0);
            }

            return null;
        }
    }
}
