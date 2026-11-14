// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http;

// NetWasm adaptation: the neutral boundary contains only managed HTTP facts.
// WASI resources and generated bindings belong behind this contract.
public interface IHttpTransport
{
    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken);
}
