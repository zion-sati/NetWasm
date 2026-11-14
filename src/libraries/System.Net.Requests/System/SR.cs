// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Requests and System.Net.WebClient use, from their Strings.resx at dotnet/runtime 811225a4, plus the NetWasm_ messages of NetWasm's adaptations.

namespace System
{
    internal static class SR
    {
        internal const string CacheEntryNotFound = "The request was aborted: The request cache-only policy does not allow a network request and the response is not found in cache.";
        internal const string net_MethodNotImplementedException = "This method is not implemented by this class.";
        internal const string net_PropertyNotImplementedException = "This property is not implemented by this class.";
        internal const string net_PropertyNotSupportedException = "This property is not supported by this class.";
        internal const string net_WebHeaderInvalidCRLFChars = "Specified value has invalid CRLF characters.";
        internal const string net_WebHeaderInvalidControlChars = "Specified value has invalid Control characters.";
        internal const string net_WebHeaderInvalidHeaderChars = "Specified value has invalid HTTP Header characters.";
        internal const string net_baddate = "The value of the date string in the header is invalid.";
        internal const string net_badmethod = "Cannot set null or blank methods on request.";
        internal const string net_completed_result = "This operation cannot be performed on a completed asynchronous result object.";
        internal const string net_connarg = "Keep-Alive and Close may not be set using this property.";
        internal const string net_fromto = "The From parameter cannot be greater than To.";
        internal const string net_ftp_no_defaultcreds = "Default credentials are not supported on an FTP request.";
        internal const string net_ftp_no_newlines = "CR and LF characters are not allowed in FtpWebRequest inputs.";
        internal const string net_ftp_unsupported_method = "This method is not supported.";
        internal const string net_ftpstatuscode_AccountNeeded = "Need account for storing files";
        internal const string net_ftpstatuscode_ActionAbortedLocalProcessingError = "Local error in processing";
        internal const string net_ftpstatuscode_ActionAbortedUnknownPageType = "Page type unknown";
        internal const string net_ftpstatuscode_ActionNotTakenFileUnavailable = "File unavailable (e.g., file not found, no access)";
        internal const string net_ftpstatuscode_ActionNotTakenFileUnavailableOrBusy = "File unavailable (e.g., file busy)";
        internal const string net_ftpstatuscode_ActionNotTakenFilenameNotAllowed = "File name not allowed";
        internal const string net_ftpstatuscode_ActionNotTakenInsufficientSpace = "Insufficient storage space in system";
        internal const string net_ftpstatuscode_ArgumentSyntaxError = "Syntax error in parameters or arguments";
        internal const string net_ftpstatuscode_BadCommandSequence = "Bad sequence of commands";
        internal const string net_ftpstatuscode_CantOpenData = "Can't open data connection";
        internal const string net_ftpstatuscode_CommandNotImplemented = "Command not implemented";
        internal const string net_ftpstatuscode_CommandSyntaxError = "Syntax error, command unrecognized";
        internal const string net_ftpstatuscode_ConnectionClosed = "Connection closed; transfer aborted";
        internal const string net_ftpstatuscode_FileActionAborted = "Exceeded storage allocation (for current directory or data set)";
        internal const string net_ftpstatuscode_NotLoggedIn = "Not logged in";
        internal const string net_ftpstatuscode_ServiceNotAvailable = "Service not available, closing control connection.";
        internal const string net_invalid_enum = "The specified value is not valid in the '{0}' enumeration.";
        internal const string net_invalid_host = "The specified value is not a valid Host header string.";
        internal const string net_io_invalidasyncresult = "The IAsyncResult object was not returned from the corresponding asynchronous method on this class.";
        internal const string net_io_invalidendcall = "{0} can only be called once for each asynchronous operation.";
        internal const string net_io_timeout_use_ge_zero = "Timeout can be only be set to 'System.Threading.Timeout.Infinite' or a value >= 0.";
        internal const string net_io_timeout_use_gt_zero = "Timeout can be only be set to 'System.Threading.Timeout.Infinite' or a value > 0.";
        internal const string net_needchunked = "TransferEncoding requires the SendChunked property to be set to true.";
        internal const string net_no100 = "100-Continue may not be set using this property.";
        internal const string net_nochunked = "Chunked encoding must be set via the SendChunked property.";
        internal const string net_nottoken = "The supplied string is not a valid HTTP token.";
        internal const string net_nouploadonget = "Cannot send a content-body with this verb-type.";
        internal const string net_rangetoosmall = "The From or To parameter cannot be less than 0.";
        internal const string net_rangetype = "A different range specifier has already been added to this request.";
        internal const string net_repcall = "Cannot re-call BeginGetRequestStream/BeginGetResponse while a previous call is still in progress.";
        internal const string net_reqaborted = "The request was aborted: The request was canceled.";
        internal const string net_reqsubmitted = "This operation cannot be performed after the request has been submitted.";
        internal const string net_requestaborted = "The request was aborted: {0}.";
        internal const string net_servererror = "The remote server returned an error: ({0}) {1}.";
        internal const string net_timeout = "The operation has timed out.";
        internal const string net_unknown_prefix = "The URI prefix is not recognized.";
        internal const string net_webclient = "An exception occurred during a WebClient request.";
        internal const string net_webclient_ContentType = "The Content-Type header cannot be changed from its default value for this request.";
        internal const string net_webclient_Multipart = "The Content-Type header cannot be set to a multipart type for this request.";
        internal const string net_webclient_invalid_baseaddress = "The specified value is not a valid base address.";
        internal const string net_webclient_no_concurrent_io_allowed = "WebClient does not support concurrent I/O operations.";
        internal const string net_webstatus_MessageLengthLimitExceeded = "The message length limit was exceeded";
        internal const string net_webstatus_Timeout = "The operation has timed out.";
        internal const string net_writestarted = "This property cannot be set after writing has started.";
        internal const string net_wrongversion = "Only HTTP/1.0 and HTTP/1.1 version requests are currently supported.";
        internal const string NetWasm_HttpWebRequestBlockingUnsupported = "Waiting synchronously for an HttpWebRequest network operation is not supported on this platform; use the asynchronous methods.";
        internal const string NetWasm_HttpWebRequestOptionUnsupported = "The HttpWebRequest setting '{0}' is not supported by this platform's HTTP transport.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string net_securityprotocolnotsupported = "The requested security protocol is not supported.";
        internal const string net_ftp_protocolerror = "The underlying connection was closed: The server committed a protocol violation";
        internal const string net_ftp_receivefailure = "The underlying connection was closed: An unexpected error occurred on a receive";
        internal const string net_ftp_response_too_large = "The FTP response exceeded the maximum allowed length of {0} characters.";
        internal const string net_ftp_servererror = "The remote server returned an error: {0}.";
        internal const string net_InvalidStatusCode = "The server returned a status code outside the valid range of 100-599.";
        internal const string net_ftp_active_address_different = "The data connection was made from an address that is different than the address to which the FTP connection was made.";
        internal const string net_ftp_invalid_response_filename = "The server returned the filename ({0}) which is not valid.";
        internal const string net_ftp_invalid_status_response = "The status response ({0}) is not expected in response to '{1}' command.";
        internal const string net_ftp_invalid_uri = "The requested URI is invalid for this FTP command.";
        internal const string net_ftp_response_invalid_format = "The response string '{0}' has invalid format.";
        internal const string net_ftp_server_failed_passive = "The server failed the passive mode request with status response ({0}).";
        internal const string net_webstatus_ConnectFailure = "Unable to connect to the remote server";
        internal const string net_webstatus_NameResolutionFailure = "The remote name could not be resolved";
    }
}
