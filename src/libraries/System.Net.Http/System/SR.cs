// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Http uses, from System.Net.Http's Strings.resx at dotnet/runtime deae2e32 (.NET 11.0.0), plus the NetWasm_ messages of NetWasm's adaptations.

// NetWasm: in System.Net.Http rather than System, where CoreLib (which grants this assembly its internals) has its own SR.
namespace System.Net.Http
{
    internal static class SR
    {
        internal const string net_http_response_headers_exceeded_length = "The HTTP response headers length exceeded the set limit of {0} bytes.";
        internal const string net_http_client_absolute_baseaddress_required = "The base address must be an absolute URI.";
        internal const string net_http_client_invalid_requesturi = "An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.";
        internal const string net_http_client_request_already_sent = "The request message was already sent. Cannot send the same request message multiple times.";
        internal const string net_http_content_buffer_stream_disposed = "The stream passed to SerializeToStream(Async) is owned by HttpContent and must not be disposed by user code.";
        internal const string net_http_content_buffersize_exceeded = "Cannot write more bytes to the buffer than the configured maximum buffer size: {0}.";
        internal const string net_http_content_buffersize_limit = "Buffering more than {0} bytes is not supported.";
        internal const string net_http_content_field_too_long = "The field cannot be longer than {0} characters.";
        internal const string net_http_content_invalid_charset = "The character set provided in ContentType is invalid. Cannot read content as string using an invalid character set.";
        internal const string net_http_content_no_task_returned = "The async operation did not return a System.Threading.Tasks.Task object.";
        internal const string net_http_content_readonly_stream = "The stream does not support writing.";
        internal const string net_http_content_stream_already_read = "The stream was already consumed. It cannot be read again.";
        internal const string net_http_content_stream_copy_error = "Error while copying content to a stream.";
        internal const string net_http_copyto_array_too_small = "The number of elements is greater than the available space from arrayIndex to the end of the destination array.";
        internal const string net_http_handler_noresponse = "Handler did not return a response message.";
        internal const string net_http_handler_not_assigned = "The inner handler has not been assigned.";
        internal const string net_http_headers_invalid_header_name = "The header name '{0}' has an invalid format.";
        internal const string net_http_headers_invalid_host_header = "The specified value is not a valid 'Host' header string.";
        internal const string net_http_headers_invalid_range = "Invalid range. At least one of the two parameters must not be null.";
        internal const string net_http_headers_invalid_value = "The format of value '{0}' is invalid.";
        internal const string net_http_headers_no_newlines_no_nul = "New-line or NUL characters are not allowed in header values.";
        internal const string net_http_headers_not_allowed_header_name = "Misused header name, '{0}'. Make sure request headers are used with HttpRequestMessage, response headers with HttpResponseMessage, and content headers with HttpContent objects.";
        internal const string net_http_headers_not_found = "The given header was not found.";
        internal const string net_http_headers_single_value_header = "Cannot add value because header '{0}' does not support multiple values.";
        internal const string net_http_httpmethod_format_error = "The format of the HTTP method is invalid.";
        internal const string net_http_invalid_enable_first = "The {0} property must be set to '{1}' to use this property.";
        internal const string net_http_log_headers_invalid_quality = "The 'q' value is invalid: '{0}'.";
        internal const string net_http_log_headers_no_newlines = "Value for header '{0}' contains new-line characters. Value: '{1}'.";
        internal const string net_http_message_not_success_statuscode = "Response status code does not indicate success: {0}.";
        internal const string net_http_message_not_success_statuscode_reason = "Response status code does not indicate success: {0} ({1}).";
        internal const string net_http_operation_started = "This instance has already started one or more requests. Properties can only be modified before sending the first request.";
        internal const string net_http_parser_invalid_base64_string = "Value '{0}' is not a valid Base64 string. Error: {1}";
        internal const string net_http_reasonphrase_format_error = "The reason phrase must not contain new-line or NUL characters.";
        internal const string net_http_request_timedout = "The request was canceled due to the configured HttpClient.Timeout of {0} seconds elapsing.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_http_missing_sync_implementation = "The synchronous method is not supported by '{0}'. If you're using a custom '{1}' and wish to use synchronous HTTP methods, you must override its '{2}' virtual method.";
    }
}
