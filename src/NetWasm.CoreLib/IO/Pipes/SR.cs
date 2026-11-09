// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de resources;
// only the strings this namespace references are included.

namespace System.IO.Pipes
{
    internal static class SR
    {
        internal const string ArgumentOutOfRange_DirectionModeInOutOrInOut =
            "For named pipes, the pipe direction can be PipeDirection.In, PipeDirection.Out or PipeDirection.InOut. For anonymous pipes, the pipe direction can be PipeDirection.In or PipeDirection.Out.";
        internal const string ArgumentOutOfRange_HandleInheritabilityNoneOrInheritable =
            "HandleInheritability.None or HandleInheritability.Inheritable required.";
        internal const string ArgumentOutOfRange_ImpersonationInvalid =
            "TokenImpersonationLevel.None, TokenImpersonationLevel.Anonymous, TokenImpersonationLevel.Identification, TokenImpersonationLevel.Impersonation or TokenImpersonationLevel.Delegation required.";
        internal const string ArgumentOutOfRange_MaxNumServerInstances =
            "maxNumberOfServerInstances must either be a value between 1 and 254, or NamedPipeServerStream.MaxAllowedServerInstances (to obtain the maximum number allowed by system resources).";
        internal const string ArgumentOutOfRange_OptionsInvalid =
            "options contains an invalid flag.";
        internal const string ArgumentOutOfRange_TransmissionModeByteOrMsg =
            "For named pipes, transmission mode can be TransmissionMode.Byte or PipeTransmissionMode.Message. For anonymous pipes, transmission mode can be TransmissionMode.Byte.";
        internal const string Argument_EmptyServerName =
            "serverName cannot be an empty string.  Use \\\\\\\".\\\\\\\" for current machine.";
        internal const string Argument_InvalidHandle =
            "Handle has been disposed or is invalid.";
        internal const string IO_EOF_ReadBeyondEOF =
            "Unable to read beyond the end of the stream.";
        internal const string IO_OperationAborted_Unexpected =
            "IO operation was aborted unexpectedly.";
        internal const string IO_PipeBroken =
            "Pipe is broken.";
        internal const string InvalidOperation_PipeAlreadyConnected =
            "Already in a connected state.";
        internal const string InvalidOperation_PipeAlreadyDisconnected =
            "Already in a disconnected state.";
        internal const string InvalidOperation_PipeDisconnected =
            "Pipe is in a disconnected state.";
        internal const string InvalidOperation_PipeHandleNotSet =
            "Pipe handle has not been set.  Did your PipeStream implementation call InitializeHandle?";
        internal const string InvalidOperation_PipeNotYetConnected =
            "Pipe hasn't been connected yet.";
        internal const string InvalidOperation_PipeReadModeNotMessage =
            "ReadMode is not of PipeTransmissionMode.Message.";
        internal const string NotSupported_AnonymousPipeMessagesNotSupported =
            "Anonymous pipes do not support PipeTransmissionMode.Message ReadMode.";
        internal const string NotSupported_AnonymousPipeUnidirectional =
            "Anonymous pipes can only be in one direction.";
        internal const string NotSupported_UnreadableStream =
            "Stream does not support reading.";
        internal const string NotSupported_UnseekableStream =
            "Stream does not support seeking.";
        internal const string NotSupported_UnwritableStream =
            "Stream does not support writing.";
        internal const string ObjectDisposed_PipeClosed =
            "Cannot access a closed pipe.";
        internal const string Pipes_PlatformNotSupported =
            "System.IO.Pipes is not supported on this platform.";

        internal static string Format(string format, object? arg0) =>
            string.Format(format, arg0);

        internal static string Format(string format, object? arg0, object? arg1) =>
            string.Format(format, arg0, arg1);

        internal static string Format(string format, object? arg0, object? arg1, object? arg2) =>
            string.Format(format, arg0, arg1, arg2);
    }
}
