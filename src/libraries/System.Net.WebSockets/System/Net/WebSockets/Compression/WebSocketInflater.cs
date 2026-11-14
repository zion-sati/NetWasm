// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm: the permessage-deflate codec needs zlib, which NetWasm does not have, so constructing
// an inflater (what ManagedWebSocket does when WebSocketCreationOptions.DangerousDeflateOptions is set)
// throws PlatformNotSupportedException and none of the other members is reached.

namespace System.Net.WebSockets.Compression
{
    internal sealed class WebSocketInflater : IDisposable
    {
        public WebSocketInflater(int windowBits, bool persisted) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public Memory<byte> Memory => throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public Span<byte> Span => throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public void Dispose()
        {
        }

        public void Prepare(long payloadLength, int userBufferLength) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public void AddBytes(int totalBytesReceived, bool endOfMessage) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);

        public bool Inflate(Span<byte> output, out int written) =>
            throw new PlatformNotSupportedException(SR.net_WebSockets_CompressionNotAvailable);
    }
}
