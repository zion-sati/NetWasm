// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Net.NetworkInformation
{
    internal static class SR
    {
        internal const string SystemNetPing_PlatformNotSupported =
            "System.Net.Ping is not supported on this platform.";
        internal const string net_MethodNotImplementedException =
            "This method is not implemented by this class.";
        internal const string net_PropertyNotImplementedException =
            "This property is not implemented by this class.";
        internal const string net_bad_mac_address =
            "An invalid physical address was specified: '{0}'.";
        internal const string net_collection_readonly =
            "The collection is read-only.";
        internal const string net_inasync =
            "An asynchronous call is already in progress. It must be completed or canceled before you can call this method.";
        internal const string net_invalidPingBufferSize =
            "The buffer length must not exceed 65500 bytes.";
        internal const string net_invalid_ip_addr =
            "IPv4 address 0.0.0.0 and IPv6 address ::0 are unspecified addresses that cannot be used as a target address.";
        internal const string net_ipv4_not_installed =
            "IPv4 is not installed.";
        internal const string net_ipv6_not_installed =
            "IPv6 is not installed.";
        internal const string net_ping =
            "An exception occurred during a Ping request.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
