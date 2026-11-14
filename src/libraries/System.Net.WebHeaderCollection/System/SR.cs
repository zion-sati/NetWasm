// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.WebHeaderCollection uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.WebHeaderCollection's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_WebHeaderInvalidCRLFChars = "Specified value has invalid CRLF characters.";
        internal const string net_WebHeaderInvalidControlChars = "Specified value has invalid Control characters.";
        internal const string net_WebHeaderInvalidHeaderChars = "Specified value '{0}' has invalid HTTP Header characters.";
        internal const string net_WebHeaderMissingColon = "Specified value does not have a ':' separator.";
        internal const string net_headers_req = "This collection holds response headers and cannot contain the specified request header.";
        internal const string net_headers_rsp = "This collection holds request headers and cannot contain the specified response header.";
    }
}
