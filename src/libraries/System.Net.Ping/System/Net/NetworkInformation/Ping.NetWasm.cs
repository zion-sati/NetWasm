// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's platform part of upstream's Ping (dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// has Unix, OSX and Windows parts and builds browser as a PlatformNotSupportedException assembly).
// NetWasm has no ICMP transport, so the send cores throw PlatformNotSupportedException, which the
// shared Ping code passes through unwrapped; the managed lifecycle, events and options stay usable.

using System.Threading.Tasks;

namespace System.Net.NetworkInformation
{
    public partial class Ping
    {
        private PingReply SendPingCore(IPAddress address, byte[] buffer, int timeout, PingOptions? options) =>
            throw new PlatformNotSupportedException(SR.SystemNetPing_PlatformNotSupported);

        private Task<PingReply> SendPingAsyncCore(IPAddress address, byte[] buffer, int timeout, PingOptions? options) =>
            throw new PlatformNotSupportedException(SR.SystemNetPing_PlatformNotSupported);
    }
}
