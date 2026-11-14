// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net
{
    internal sealed class RequestStreamContent(TaskCompletionSource<Stream> getStreamTcs, TaskCompletionSource completeTcs) : HttpContent
    {
        // NetWasm: System.Net.Http keeps the cancellation overload internal (it is newer than netstandard
        // 2.1), so this assembly overrides the public one; the request completes or aborts through completeTcs.
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Debug.Assert(stream is not null);

            getStreamTcs.TrySetResult(stream);
            await completeTcs.Task.ConfigureAwait(false);
        }
        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
