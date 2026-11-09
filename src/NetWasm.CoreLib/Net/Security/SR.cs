// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Net.Security
{
    internal static class SR
    {
        internal const string net_invalid_enum =
            "The specified value is not valid in the '{0}' enumeration.";
        internal const string net_io_must_be_rw_stream =
            "The stream has to be read/write.";
        internal const string net_negotiate_NotSupported =
            "NegotiateStream is not available on this NetWasm platform: there is no host Negotiate, NTLM or Kerberos backend.";
        internal const string net_ssl_NotSupported =
            "SslStream is not available on this NetWasm platform: there is no host TLS backend.";
        internal const string net_ssl_app_protocol_invalid =
            "The application protocol value is invalid.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
