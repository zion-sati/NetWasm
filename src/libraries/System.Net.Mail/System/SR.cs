// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Net.Mail uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Net.Mail's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string MailBase64InvalidCharacter = "An invalid character was found in the Base-64 stream.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string MailHeaderFieldInvalidCharacter = "An invalid character was found in the mail header: '{0}'.";
        internal const string ReadNotSupported = "Reading is not supported on this stream.";
        internal const string SeekNotSupported = "Seeking is not supported on this stream.";
        internal const string WriteNotSupported = "Writing is not supported on this stream.";
        internal const string MailHeaderInvalidCID = "The ContentID cannot contain a '<' or '>' character.";
        internal const string MediaTypeInvalid = "The specified media type is invalid.";
        internal const string MailAddressInvalidFormat = "The specified string is not in the form required for an e-mail address.";
        internal const string SmtpInvalidHostName = "The address has an invalid host name: {0}.";
        internal const string SmtpNonAsciiUserNotSupported = "The client or server is only configured for E-mail addresses with ASCII local-parts: {0}.";
        internal const string InvalidHeaderName = "An invalid character was found in header name.";
        internal const string MailHeaderFieldMalformedHeader = "The mail header is malformed.";
        internal const string MailSubjectInvalidFormat = "The specified string is not in the form required for a subject.";
        internal const string MailServerResponse = "The server response was: {0}";
        internal const string SmtpBadCommandSequence = "Bad sequence of commands.";
        internal const string SmtpCannotVerifyUserWillAttemptDelivery = "Cannot verify user, but will attempt delivery.";
        internal const string SmtpClientNotPermitted = "Client does not have permission to submit mail to this server.";
        internal const string SmtpCommandNotImplemented = "Command not implemented.";
        internal const string SmtpCommandParameterNotImplemented = "Command parameter not implemented.";
        internal const string SmtpCommandUnrecognized = "Syntax error, command unrecognized.";
        internal const string SmtpExceededStorageAllocation = "Exceeded storage allocation.";
        internal const string SmtpGeneralFailure = "General failure.";
        internal const string SmtpHelpMessage = "Help message.";
        internal const string SmtpInsufficientStorage = "Insufficient system storage.";
        internal const string SmtpLocalErrorInProcessing = "Error in processing.";
        internal const string SmtpMailboxBusy = "Mailbox unavailable.";
        internal const string SmtpMailboxNameNotAllowed = "Mailbox name not allowed.";
        internal const string SmtpMailboxUnavailable = "Mailbox unavailable.";
        internal const string SmtpMustIssueStartTlsFirst = "The SMTP server requires a secure connection or the client was not authenticated.";
        internal const string SmtpOK = "Completed.";
        internal const string SmtpServiceClosingTransmissionChannel = "Service closing transmission channel.";
        internal const string SmtpServiceNotAvailable = "Service not available, closing transmission channel.";
        internal const string SmtpServiceReady = "Service ready.";
        internal const string SmtpStartMailInput = "Start mail input; end with <CRLF>.<CRLF>.";
        internal const string SmtpSyntaxError = "Syntax error in parameters or arguments.";
        internal const string SmtpSystemStatus = "System status, or system help reply.";
        internal const string SmtpTransactionFailed = "Transaction failed.";
        internal const string SmtpUnknownStatusCode = "Unknown SMTP status code.";
        internal const string SmtpUserNotLocalTryAlternatePath = "User not local; please try a different path.";
        internal const string SmtpUserNotLocalWillForward = "User not local; will forward to specified path.";
        internal const string SmtpAllRecipientsFailed = "Unable to send to all recipients.";
        internal const string SmtpRecipientFailed = "Unable to send to a recipient.";
        internal const string MailWriterIsInContent = "This operation cannot be performed while in content.";
        internal const string ContentDispositionInvalid = "The specified content disposition is invalid.";
        internal const string ContentTypeInvalid = "The specified content type is invalid.";
        internal const string MimePartCantResetStream = "One of the streams has already been used and can't be reset to the origin.";
        internal const string MimeTransferEncodingNotSupported = "The MIME transfer encoding '{0}' is not supported.";
        internal const string InvalidHexDigit = "Invalid hex digit '{0}'.";
        internal const string MailDateInvalidFormat = "The date is in an invalid format.";
        internal const string MailCollectionIsReadOnly = "The collection is read-only.";
        internal const string InvalidPort = "The specified port is invalid. The port must be greater than 0.";
        internal const string SmtpFromRequired = "A from address must be specified.";
        internal const string SmtpGetIisPickupDirectoryNotSupported = "IIS delivery is not supported.";
        internal const string SmtpInvalidOperationDuringSend = "This operation cannot be performed while a message is being sent.";
        internal const string SmtpNeedAbsolutePickupDirectory = "Only absolute directories are allowed for pickup directory.";
        internal const string SmtpPickupDirectoryDoesnotSupportSsl = "SSL must not be enabled for pickup-directory delivery methods.";
        internal const string SmtpRecipientRequired = "A recipient must be specified.";
        internal const string SmtpSendMailFailure = "Failure sending mail.";
        internal const string UnspecifiedHost = "The SMTP host was not specified.";
        internal const string net_inasync = "An asynchronous call is already in progress. It must be completed or canceled before you can call this method.";
        internal const string net_timeout = "The operation has timed out.";
        internal const string SmtpAuthResponseInvalid = "The server returned an invalid response in the authentication handshake.";
        internal const string SmtpDataStreamOpen = "Data stream is still open.";
        internal const string SmtpEhloResponseInvalid = "The server returned an invalid response to the EHLO command.";
        internal const string net_webstatus_ServerProtocolViolation = "The server committed a protocol violation.";
        internal const string MailServerDoesNotSupportStartTls = "Server does not support secure connections.";
        internal const string SmtpAlreadyConnected = "Already connected.";
        internal const string SmtpAuthenticationFailed = "Authentication failed.";
        internal const string SmtpInvalidResponse = "Smtp server returned an invalid response.";
        internal const string net_io_connectionclosed = "The connection was closed";
        internal const string net_io_readfailure = "Unable to read data from the transport connection: {0}.";
    }
}
