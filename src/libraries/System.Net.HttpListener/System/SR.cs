// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.HttpListener uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.HttpListener's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string SystemNetHttpListener_PlatformNotSupported = "System.Net.HttpListener is not supported on this platform.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_WebSockets_InvalidCharInProtocolString = "The WebSocket protocol '{0}' is invalid because it contains the invalid character '{1}'.";
        internal const string net_WebSockets_InvalidCloseStatusCode = "The close status code '{0}' is reserved for system use only and cannot be specified when calling this method.";
        internal const string net_WebSockets_InvalidCloseStatusDescription = "The close status description '{0}' is too long. The UTF-8 representation of the status description must not be longer than {1} bytes.";
        internal const string net_WebSockets_ReasonNotNull = "The close status description '{0}' is invalid. When using close status code '{1}' the description must be null.";
        internal const string net_listener_cannot_set_custom_cbt = "Custom channel bindings are not supported.";
        internal const string net_listener_host = "Only Uri prefixes with a valid hostname are supported.";
        internal const string net_listener_scheme = "Only Uri prefixes starting with 'http://' or 'https://' are supported.";
        internal const string net_listener_slash = "Only Uri prefixes ending in '/' are allowed.";
        internal const string net_array_too_small = "The target array is too small.";
        internal const string net_listener_callinprogress = "Cannot re-call {0} while a previous call is still in progress.";
        internal const string net_listener_mustcall = "Please call the {0} method before calling this method.";
        internal const string net_listener_mustcompletecall = "The in-progress method {0} must be completed first.";
        internal const string net_log_listener_cant_convert_bytes = "Can't convert bytes '{0}' into UTF-16 characters: {1}";
        internal const string net_log_listener_cant_convert_percent_value = "Can't convert percent encoded value '{0}'.";
        internal const string net_log_listener_cant_convert_raw_path = "Can't convert Uri path '{0}' using encoding '{1}'.";
        internal const string net_log_listener_cant_convert_to_utf8 = "Can't convert string '{0}' into UTF-8 bytes: {1}";
        internal const string net_log_listener_cant_create_uri = "Can't create Uri from string '{0}://{1}{2}{3}'.";
        internal const string net_WebHeaderInvalidControlChars = "Specified value has invalid Control characters.";
        internal const string net_cookie_exists = "Cookie already exists.";
        internal const string net_nochunkuploadonhttp10 = "Chunked encoding upload is not supported on the HTTP/1.0 protocol.";
        internal const string net_rspsubmitted = "This operation cannot be performed after the response has been submitted.";
        internal const string net_noseek = "This stream does not support seek operations.";
        internal const string net_readonlystream = "The stream does not support writing.";
        internal const string net_writeonlystream = "The stream does not support reading.";
        internal const string net_io_invalidasyncresult = "The IAsyncResult object was not returned from the corresponding asynchronous method on this class.";
        internal const string net_io_invalidendcall = "{0} can only be called once for each asynchronous operation.";
        internal const string net_io_operation_aborted = "I/O operation aborted: '{0}'.";
        internal const string net_listener_already = "Failed to listen on prefix '{0}' because it conflicts with an existing registration on the machine.";
        internal const string net_invalid_path = "Invalid path.";
        internal const string net_invalid_port = "Invalid port in prefix.";
        internal const string net_listener_not_supported = "The request is not supported.";
        internal const string net_listener_invalid_cbt_type = "Querying the {0} Channel Binding is not supported.";
        internal const string net_invalidstatus = "The status code must be exactly three digits.";
        internal const string net_wrongversion = "Only HTTP/1.0 and HTTP/1.1 version requests are currently supported.";
        internal const string net_listener_auth_errors = "Authentication errors.";
        internal const string net_listener_close = "Listener closed.";
        internal const string net_log_listener_spn_add = "Adding default service name '{0}' from prefix '{1}'.";
        internal const string net_log_listener_spn_not_add = "No default service name added for prefix '{0}'.";
        internal const string net_log_listener_spn_not_remove = "No default service name removed for prefix '{0}'.";
        internal const string net_log_listener_spn_remove = "Removing default service name '{0}' from prefix '{1}'.";
        internal const string net_WebSockets_AcceptHeaderNotFound = "The {0} operation was called on an incoming WebSocket request without required '{1}' header. ";
        internal const string net_WebSockets_AcceptNotAWebSocket = "The {0} operation was called on an incoming request that did not specify a '{1}: {2}' header or the {2} header not contain '{3}'. {2} specified by the client was '{4}'.";
        internal const string net_WebSockets_AcceptUnsupportedProtocol = "The WebSocket client request requested '{0}' protocol(s), but server is only accepting '{1}' protocol(s).";
        internal const string net_WebSockets_AcceptUnsupportedWebSocketVersion = "The {0} operation was called on an incoming request with WebSocket version '{1}', expected '{2}'. ";
        internal const string net_WebSockets_ClientAcceptingNoProtocols = "The WebSocket client did not request any protocols, but server attempted to accept '{0}' protocol(s). ";
        internal const string net_WebSockets_UnsupportedPlatform = "The WebSocket protocol is not supported on this platform.";
    }
}
