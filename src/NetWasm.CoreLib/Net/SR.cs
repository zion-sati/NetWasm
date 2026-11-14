// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included.

namespace System.Net
{
    internal static class SR
    {
        internal const string InvalidOperation_EnumFailedVersion =
            "Collection was modified after the enumerator was instantiated.";
        internal const string InvalidOperation_EnumOpCantHappen =
            "Enumeration has either not started or has already finished.";
        internal const string bad_endpoint_string =
            "An invalid IPEndPoint was specified.";
        internal const string net_InvalidAddressFamily =
            "The AddressFamily {0} is not valid for the {1} end point, use {2} instead.";
        internal const string net_InvalidSocketAddressSize =
            "The supplied {0} is an invalid size for the {1} end point.";
        internal const string net_WebHeaderInvalidCRLFChars =
            "Specified value has invalid CRLF characters.";
        internal const string net_WebHeaderInvalidControlChars =
            "Specified value has invalid Control characters.";
        internal const string net_WebHeaderInvalidHeaderChars =
            "Specified value '{0}' has invalid HTTP Header characters.";
        internal const string net_container_add_cookie =
            "An error occurred when adding a cookie to the container.";
        internal const string net_cookie_attribute =
            "The '{0}'='{1}' part of the cookie is invalid.";
        internal const string net_cookie_capacity_range =
            "'{0}' has to be greater than '{1}' and less than '{2}'.";
        internal const string net_cookie_format =
            "Cookie format error.";
        internal const string net_cookie_parse_header =
            "An error occurred when parsing the Cookie header for Uri '{0}'.";
        internal const string net_cookie_size =
            "The value size of the cookie is '{0}'. This exceeds the configured maximum size, which is '{1}'.";
        internal const string net_emptystringcall =
            "The parameter '{0}' cannot be an empty string.";
        internal const string net_hostname_invalid_character =
            "The host name contains an invalid character.";
        internal const string net_invalid_ip_addr =
            "IPv4 address 0.0.0.0 and IPv6 address ::0 are unspecified addresses that cannot be used as a target address.";
        internal const string net_sockets_invalid_optionValue_all =
            "The specified value is not valid.";
        internal const string net_toolong =
            "The size of {0} is too long. It cannot be longer than {1} characters.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);

        internal static string Format(string format, params object?[] args) =>
            string.Format(format, args);
    }
}
