// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Net.Http.WasiHttp;

// NetWasm: HttpClientHandler's default transport, over wasi:http 0.3's client.
internal static class WasiHttpTransportComposition
{
    internal static IHttpTransport Create() =>
        new WasiHttpTransport(new WasiHttpBindingClient(), new WasiHttpFailureMapper());
}
