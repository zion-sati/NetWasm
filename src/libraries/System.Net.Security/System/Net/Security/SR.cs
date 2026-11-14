// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
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
        internal const string net_auth_noauth = "This operation is only allowed using a successfully authenticated context.";
        internal const string net_extprotection_not_supported = "Extended Protection is not supported on this platform.";
        internal const string net_log_listener_no_spn_cbt = "No service name check because the channel binding was already checked.";
        internal const string net_log_listener_no_spn_disabled = "No service name check because extended protection is disabled.";
        internal const string net_log_listener_no_spn_kerberos = "No explicit service name check because Kerberos authentication already validates the service name.";
        internal const string net_log_listener_no_spn_whensupported = "No service name check because the client did not provide a service name and the server was configured for PolicyEnforcement.WhenSupported.";
        internal const string net_log_listener_no_spns = "No service names could be determined from the registered prefixes. Specify an ExtendedProtectionPolicy object which contains an explicit list of service names.";
        internal const string net_log_listener_spn = "Client provided service name '{0}'.";
        internal const string net_log_listener_spn_failed = "Service name check failed.";
        internal const string net_log_listener_spn_failed_always = "Service name check failed because the client did not provide a service name and the server was configured for PolicyEnforcement.Always.";
        internal const string net_log_listener_spn_failed_dump = "Dumping acceptable service names:";
        internal const string net_log_listener_spn_failed_empty = "No acceptable service names were configured!";
        internal const string net_log_listener_spn_passed = "Service name check succeeded.";
    }
}
