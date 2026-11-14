// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Ping uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.Ping's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string SystemNetPing_PlatformNotSupported = "System.Net.Ping is not supported on this platform.";
        internal const string net_inasync = "An asynchronous call is already in progress. It must be completed or canceled before you can call this method.";
        internal const string net_invalidPingBufferSize = "The buffer length must not exceed 65500 bytes.";
        internal const string net_invalid_ip_addr = "IPv4 address 0.0.0.0 and IPv6 address ::0 are unspecified addresses that cannot be used as a target address.";
        internal const string net_ipv4_not_installed = "IPv4 is not installed.";
        internal const string net_ipv6_not_installed = "IPv6 is not installed.";
        internal const string net_ping = "An exception occurred during a Ping request.";
    }
}
