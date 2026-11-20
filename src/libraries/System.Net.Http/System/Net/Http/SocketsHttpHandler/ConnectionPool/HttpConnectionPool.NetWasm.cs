// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Net.Http
{
    // NetWasm: the pool's Basic and Digest authentication goes through AuthenticationHelper's IHttpAuthenticationPool.
    internal sealed partial class HttpConnectionPool : IHttpAuthenticationPool
    {
    }
}
