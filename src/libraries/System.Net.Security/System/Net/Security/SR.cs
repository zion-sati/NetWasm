// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce resources;
// only the strings this namespace references are included, and NetWasm's own for what wasi:tls lacks.

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
        internal const string net_ssl_SupportSwitchedOff =
            "SslStream is not available: this program was built with NetWasmTlsSupport set to false, for a host without wasi:tls.";
        internal const string net_ssl_server_NotSupported =
            "SslStream cannot authenticate as a server on this NetWasm platform: wasi:tls is client-only.";
        internal const string net_ssl_client_certificates_NotSupported =
            "SslStream cannot send client certificates on this NetWasm platform: wasi:tls has no client certificates.";
        internal const string net_ssl_null_encryption_NotSupported =
            "EncryptionPolicy.NoEncryption is not supported on this NetWasm platform.";
        internal const string net_noseek =
            "This stream does not support seek operations.";
        internal const string net_io_invalidnestedcall =
            " This method may not be called when another {0} operation is pending.";
        internal const string net_ssl_io_already_shutdown =
            "Write operations are not allowed after the channel was shutdown.";
        internal const string net_auth_reauth =
            "This operation is not allowed on a security context that has already been authenticated.";
        internal const string net_conflicting_options =
            "The '{0}' option was already set in the SslStream constructor.";
        internal const string net_io_read =
            "The read operation failed, see inner exception.";
        internal const string net_io_write =
            "The write operation failed, see inner exception.";
        internal const string net_io_decrypt =
            "The decryption operation failed, see inner exception.";
        internal const string net_auth_SSPI =
            "Authentication failed, see inner exception.";
        internal const string net_ssl_io_cert_custom_validation =
            "The remote certificate was rejected by the provided RemoteCertificateValidationCallback.";
        internal const string net_securityprotocolnotsupported =
            "The requested security protocol is not supported.";
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
