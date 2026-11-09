// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.Net
{
    internal static class SR
    {
        internal const string ContentDispositionInvalid =
            "The specified content disposition is invalid.";
        internal const string ContentTypeInvalid =
            "The specified content type is invalid.";
        internal const string InvalidHeaderName =
            "An invalid character was found in header name.";
        internal const string InvalidHexDigit =
            "Invalid hex digit '{0}'.";
        internal const string InvalidOperation_EnumFailedVersion =
            "Collection was modified after the enumerator was instantiated.";
        internal const string InvalidOperation_EnumOpCantHappen =
            "Enumeration has either not started or has already finished.";
        internal const string MailAddressInvalidFormat =
            "The specified string is not in the form required for an e-mail address.";
        internal const string MailBase64InvalidCharacter =
            "An invalid character was found in the Base-64 stream.";
        internal const string MailCollectionIsReadOnly =
            "The collection is read-only.";
        internal const string MailDateInvalidFormat =
            "The date is in an invalid format.";
        internal const string MailHeaderFieldInvalidCharacter =
            "An invalid character was found in the mail header: '{0}'.";
        internal const string MailHeaderFieldMalformedHeader =
            "The mail header is malformed.";
        internal const string MailHeaderInvalidCID =
            "The ContentID cannot contain a '<' or '>' character.";
        internal const string MailServerResponse =
            "The server response was: {0}";
        internal const string MailSubjectInvalidFormat =
            "The specified string is not in the form required for a subject.";
        internal const string MailWriterIsInContent =
            "This operation cannot be performed while in content.";
        internal const string MediaTypeInvalid =
            "The specified media type is invalid.";
        internal const string MimePartCantResetStream =
            "One of the streams has already been used and can't be reset to the origin.";
        internal const string MimeTransferEncodingNotSupported =
            "The MIME transfer encoding '{0}' is not supported.";
        internal const string ReadNotSupported =
            "Reading is not supported on this stream.";
        internal const string SeekNotSupported =
            "Seeking is not supported on this stream.";
        internal const string SmtpAllRecipientsFailed =
            "Unable to send to all recipients.";
        internal const string SmtpBadCommandSequence =
            "Bad sequence of commands.";
        internal const string SmtpCannotVerifyUserWillAttemptDelivery =
            "Cannot verify user, but will attempt delivery.";
        internal const string SmtpClientNotPermitted =
            "Client does not have permission to submit mail to this server.";
        internal const string SmtpCommandNotImplemented =
            "Command not implemented.";
        internal const string SmtpCommandParameterNotImplemented =
            "Command parameter not implemented.";
        internal const string SmtpCommandUnrecognized =
            "Syntax error, command unrecognized.";
        internal const string SmtpExceededStorageAllocation =
            "Exceeded storage allocation.";
        internal const string SmtpGeneralFailure =
            "General failure.";
        internal const string SmtpHelpMessage =
            "Help message.";
        internal const string SmtpInsufficientStorage =
            "Insufficient system storage.";
        internal const string SmtpInvalidHostName =
            "The address has an invalid host name: {0}.";
        internal const string SmtpLocalErrorInProcessing =
            "Error in processing.";
        internal const string SmtpMailboxBusy =
            "Mailbox unavailable.";
        internal const string SmtpMailboxNameNotAllowed =
            "Mailbox name not allowed.";
        internal const string SmtpMailboxUnavailable =
            "Mailbox unavailable.";
        internal const string SmtpMustIssueStartTlsFirst =
            "The SMTP server requires a secure connection or the client was not authenticated.";
        internal const string SmtpNonAsciiUserNotSupported =
            "The client or server is only configured for E-mail addresses with ASCII local-parts: {0}.";
        internal const string SmtpOK =
            "Completed.";
        internal const string SmtpRecipientFailed =
            "Unable to send to a recipient.";
        internal const string SmtpServiceClosingTransmissionChannel =
            "Service closing transmission channel.";
        internal const string SmtpServiceNotAvailable =
            "Service not available, closing transmission channel.";
        internal const string SmtpServiceReady =
            "Service ready.";
        internal const string SmtpStartMailInput =
            "Start mail input; end with <CRLF>.<CRLF>.";
        internal const string SmtpSyntaxError =
            "Syntax error in parameters or arguments.";
        internal const string SmtpSystemStatus =
            "System status, or system help reply.";
        internal const string SmtpTransactionFailed =
            "Transaction failed.";
        internal const string SmtpUnknownStatusCode =
            "Unknown SMTP status code.";
        internal const string SmtpUserNotLocalTryAlternatePath =
            "User not local; please try a different path.";
        internal const string SmtpUserNotLocalWillForward =
            "User not local; will forward to specified path.";
        internal const string SystemNetHttpListener_PlatformNotSupported =
            "System.Net.HttpListener is not supported on this platform.";
        internal const string WriteNotSupported =
            "Writing is not supported on this stream.";
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
        internal const string net_WebHeaderMissingColon =
            "Specified value does not have a ':' separator.";
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
        internal const string net_headers_req =
            "This collection holds response headers and cannot contain the specified request header.";
        internal const string net_headers_rsp =
            "This collection holds request headers and cannot contain the specified response header.";
        internal const string net_hostname_invalid_character =
            "The host name contains an invalid character.";
        internal const string net_invalid_ip_addr =
            "IPv4 address 0.0.0.0 and IPv6 address ::0 are unspecified addresses that cannot be used as a target address.";
        internal const string net_nodefaultcreds =
            "Default credentials cannot be supplied for the {0} authentication scheme.";
        internal const string net_securityprotocolnotsupported =
            "The requested security protocol is not supported.";
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
