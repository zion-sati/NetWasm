// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: the permessage-deflate codec needs zlib, which NetWasm does not have, so constructing
// a deflater (what ManagedWebSocket does when WebSocketCreationOptions.DangerousDeflateOptions is set)
// throws PlatformNotSupportedException and none of the other members is reached.

namespace System.Net.WebSockets.Compression
{
    internal sealed class WebSocketDeflater : IDisposable
    {
        public WebSocketDeflater(int windowBits, bool persisted) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public void Dispose()
        {
        }

        public void ReleaseBuffer() => throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public ReadOnlySpan<byte> Deflate(ReadOnlySpan<byte> payload, bool endOfMessage) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);
    }
}
