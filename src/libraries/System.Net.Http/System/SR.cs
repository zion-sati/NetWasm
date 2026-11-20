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
        internal const string net_http_content_read_as_stream_has_task = "The content's stream has already been retrieved via async ReadAsStreamAsync and cannot be subsequently accessed synchronously.";
        internal const string net_invalid_enum = "The specified value is not valid in the '{0}' enumeration.";
        internal const string net_http_missing_sync_implementation = "The synchronous method is not supported by '{0}'. If you're using a custom '{1}' and wish to use synchronous HTTP methods, you must override its '{2}' virtual method.";
        internal const string ObjectDisposed_StreamClosed = "Cannot access a closed stream.";
        internal const string net_http_authconnectionfailure = "Authentication failed because the connection could not be reused.";
        internal const string net_http_authvalidationfailure = "Authentication validation failed with error - {0}.";
        internal const string net_http_chunk_too_large = "The HTTP/1.1 response chunk was too large.";
        internal const string net_http_chunked_not_allowed_with_empty_content = "'Transfer-Encoding: chunked' header can not be used when content object is not specified.";
        internal const string net_http_client_execution_error = "An error occurred while sending the request.";
        internal const string net_http_connect_timedout = "A connection could not be established within the configured ConnectTimeout.";
        internal const string net_http_content_write_larger_than_content_length = "Unable to write content to request stream; content would exceed Content-Length.";
        internal const string net_http_content_writeonly_stream = "The stream does not support reading.";
        internal const string net_http_disposed_while_in_use = "The object was disposed while operations were in progress.";
        internal const string net_http_exception_during_plaintext_filter = "An exception occurred while invoking the PlaintextStreamFilter.";
        internal const string net_http_headers_exceeded_length = "The HTTP headers length exceeded the set limit of {0} bytes.";
        internal const string net_http_hpack_bad_integer = "HPACK integer exceeds limits or has an overlong encoding.";
        internal const string net_http_hpack_huffman_decode_failed = "Huffman-coded literal string failed to decode.";
        internal const string net_http_hpack_incomplete_header_block = "Incomplete header block received.";
        internal const string net_http_hpack_invalid_index = "Invalid header index: {0} is outside of static table and no dynamic table entry found.";
        internal const string net_http_hpack_large_table_size_update = "Dynamic table size update to {0} bytes exceeds limit of {1} bytes.";
        internal const string net_http_hpack_late_dynamic_table_size_update = "Dynamic table size update received after beginning of header block.";
        internal const string net_http_hpack_unexpected_end = "End of headers reached with incomplete token.";
        internal const string net_http_http2_connection_close = "The HTTP/2 server closed the connection. HTTP/2 error code '{0}' (0x{1}).";
        internal const string net_http_http2_connection_error = "The HTTP/2 server sent invalid data on the connection. HTTP/2 error code '{0}' (0x{1}).";
        internal const string net_http_http2_connection_not_established = "An HTTP/2 connection could not be established because the server did not complete the HTTP/2 handshake.";
        internal const string net_http_http2_frame_limit_exceeded = "The HTTP/2 connection was aborted because the size of the frame queue exceeded an internal limit.";
        internal const string net_http_http2_invalidinitialstreamwindowsize = "The initial HTTP/2 stream window size must be between {0} and {1}.";
        internal const string net_http_http2_stream_error = "The HTTP/2 server reset the stream. HTTP/2 error code '{0}' (0x{1}).";
        internal const string net_http_http2_sync_not_supported = "The synchronous method is not supported by '{0}' for HTTP/2 or higher. Either use an asynchronous method or downgrade the request version to HTTP/1.1 or lower.";
        internal const string net_http_http3_connection_error = "The HTTP/3 server sent invalid data on the connection. HTTP/3 error code '{0}' (0x{1}).";
        internal const string net_http_http3_stream_error = "The HTTP/3 server reset the stream. HTTP/3 error code '{0}' (0x{1}).";
        internal const string net_http_invalid_header_name = "Received an invalid header name: '{0}'.";
        internal const string net_http_invalid_proxy_scheme = "Only the 'http', 'https', 'socks4', 'socks4a', 'socks5', and 'socks5h' schemes are allowed for proxies.";
        internal const string net_http_invalid_response = "The server returned an invalid or unrecognized response.";
        internal const string net_http_invalid_response_chunk_extension_invalid = "Received an invalid chunk extension: '{0}'.";
        internal const string net_http_invalid_response_chunk_header_invalid = "Received chunk header length could not be parsed: '{0}'.";
        internal const string net_http_invalid_response_chunk_line_ending = "Received an invalid chunk line ending. Chunked encoding lines must be terminated by CRLF.";
        internal const string net_http_invalid_response_chunk_terminator_invalid = "Received an invalid chunk terminator: '{0}'.";
        internal const string net_http_invalid_response_header_line = "Received an invalid header line: '{0}'.";
        internal const string net_http_invalid_response_header_name = "Received an invalid header name: '{0}'.";
        internal const string net_http_invalid_response_missing_frame = "The response ended prematurely while waiting for the next frame from the server.";
        internal const string net_http_invalid_response_multiple_status_codes = "The response contained more than one status code.";
        internal const string net_http_invalid_response_premature_eof = "The response ended prematurely.";
        internal const string net_http_invalid_response_premature_eof_bytecount = "The response ended prematurely, with at least {0} additional bytes expected.";
        internal const string net_http_invalid_response_pseudo_header_in_trailer = "Received an HTTP/2 pseudo-header as a trailing header.";
        internal const string net_http_invalid_response_status_code = "Received an invalid status code: '{0}'.";
        internal const string net_http_invalid_response_status_line = "Received an invalid status line: '{0}'.";
        internal const string net_http_invalid_response_status_reason = "Received status phrase could not be decoded with iso-8859-1: '{0}'.";
        internal const string net_http_null_from_connect_callback = "The user-supplied ConnectCallback returned null.";
        internal const string net_http_null_from_plaintext_filter = "The user-supplied PlaintextStreamFilter returned null.";
        internal const string net_http_proxy_tunnel_error = "An error occurred while establishing a connection to the proxy tunnel.";
        internal const string net_http_proxy_tunnel_returned_failure_status_code = "The proxy tunnel request to proxy '{0}' failed with status code '{1}'.\"";
        internal const string net_http_request_aborted = "The request was aborted.";
        internal const string net_http_request_content_length_mismatch = "Sent {0} request content bytes, but Content-Length promised {1}.";
        internal const string net_http_request_headers_exceeded_length = "The HTTP request headers length exceeded the server limit of {0} bytes.";
        internal const string net_http_request_invalid_char_encoding = "Request headers must contain only ASCII characters.";
        internal const string net_http_request_invalid_host_punycode = "Request Uri's host could not be Punycode encoded.";
        internal const string net_http_request_no_host = "CONNECT request must contain Host header.";
        internal const string net_http_requested_version_cannot_establish = "Requesting HTTP version {0} with version policy {1} while unable to establish HTTP/{2} connection.";
        internal const string net_http_requested_version_server_refused = "Requesting HTTP version {0} with version policy {1} while server offers only version fallback.";
        internal const string net_http_server_shutdown = "The server shut down the connection.";
        internal const string net_http_ssl_connection_failed = "The SSL connection could not be established, see inner exception.";
        internal const string net_http_unsupported_chunking = "HTTP 1.0 does not support chunking.";
        internal const string net_http_unsupported_requesturi_scheme = "The '{0}' scheme is not supported.";
        internal const string net_http_unsupported_version = "Request version value must be one of 1.0, 1.1, 2.0, or 3.0.";
        internal const string net_http_upgrade_not_enabled_sync = "HTTP request version upgrade is not enabled for synchronous '{0}'. Do not use '{1}' version policy for synchronous HTTP methods.";
        internal const string net_http_value_must_be_greater_than_or_equal = "The specified value '{0}' must be greater than or equal to '{1}'.";
        internal const string net_ping_request_timed_out = "The HTTP/2 server didn't respond to a ping request within the configured KeepAlivePingDelay.";
        internal const string net_socks_auth_failed = "Failed to authenticate with the SOCKS server.";
        internal const string net_socks_auth_required = "SOCKS server requested username & password authentication.";
        internal const string net_socks_bad_address_type = "SOCKS server returned an unknown address type.";
        internal const string net_socks_connection_failed = "SOCKS server failed to connect to the destination. Received error code 0x{0}.";
        internal const string net_socks_ipv6_notsupported = "SOCKS4 does not support IPv6 addresses.";
        internal const string net_socks_no_auth_method = "SOCKS server did not return a suitable authentication method.";
        internal const string net_socks_no_ipv4_address = "Failed to resolve the destination host to an IPv4 address.";
        internal const string net_socks_string_too_long = "Encoding the {0} took more than the maximum of 255 bytes.";
        internal const string net_socks_unexpected_version = "Unexpected SOCKS protocol version. Required {0}, got {1}.";
        internal const string net_ssl_http2_requires_tls12 = "HTTP/2 requires TLS 1.2 or newer, but '{0}' was negotiated.";
        internal const string net_unsupported_extended_connect = "The HTTP/2 server does not support extended CONNECT requests. Try to downgrade the request version to HTTP/1.1.";
    }
}
