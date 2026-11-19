// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    // NetWasm: what AuthenticationHelper's Basic and Digest authentication asks of a pool, so that SocketsHttpHandler's
    // pool (over sockets) and WasiHttpHandler's (over wasi:http) share it. Connection-level authentication (NTLM,
    // Negotiate) stays with SocketsHttpHandler's pool.
    internal interface IHttpAuthenticationPool
    {
        PreAuthCredentialCache? PreAuthCredentials { get; }

        ValueTask<HttpResponseMessage> SendWithVersionDetectionAndRetryAsync(HttpRequestMessage request, bool async, bool doRequestAuth, CancellationToken cancellationToken);

        ValueTask<HttpResponseMessage> SendWithProxyAuthAsync(HttpRequestMessage request, bool async, bool doRequestAuth, CancellationToken cancellationToken);
    }
}
