// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Diagnostics.Process uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Diagnostics.Process's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string ProcessHostNotSupported = "Operating system processes are not available on this NetWasm platform.";
        internal const string ProcessStartWithPasswordAndDomainNotSupported = "Starting a process as another user with a specified password and domain is not supported on this platform.";
        internal const string CantGetProcessStartInfo = "Process was not started by this object, so requested information cannot be determined.";
        internal const string CantGetStandardError = "StandardError has not been redirected.";
        internal const string CantGetStandardIn = "StandardIn has not been redirected.";
        internal const string CantGetStandardOut = "StandardOut has not been redirected or the process hasn't started yet.";
        internal const string CantMixSyncAsyncOperation = "Cannot mix synchronous and asynchronous operation on process stream.";
        internal const string CantSetProcessStartInfo = "Process is already associated with a real process, so the requested operation cannot be performed.";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string MissingProcess = "Process with an Id of {0} is not running.";
        internal const string NoAssociatedProcess = "No process is associated with this object.";
        internal const string NoAsyncOperation = "No async read operation is in progress on the stream.";
        internal const string NoProcessHandle = "Process was not started by this object, so requested information cannot be determined.";
        internal const string NoProcessInfo = "Process has exited, so the requested information is not available.";
        internal const string NotSupportedRemote = "Feature is not supported for remote machines.";
        internal const string PendingAsyncOperation = "An async read operation has already been started on the stream.";
        internal const string ProcessIdRequired = "Feature requires a process identifier.";
        internal const string WaitTillExit = "Process must exit before requested information can be determined.";
        internal const string RemoteMachinesNotSupported = "Access to processes on remote machines is not supported on this platform.";
        internal const string ProcessStartSingleFeatureNotSupported = "The {0} property is not supported on this platform.";
        internal const string ArgumentAndArgumentListInitialized = "Only one of Arguments or ArgumentList may be used.";
        internal const string CantRedirectStreams = "The Process object must have the UseShellExecute property set to false in order to redirect IO streams.";
        internal const string FileNameMissing = "Cannot start process because a file name has not been provided.";
        internal const string StandardErrorEncodingNotAllowed = "StandardErrorEncoding is only supported when standard error is redirected.";
        internal const string StandardInputEncodingNotAllowed = "StandardInputEncoding is only supported when standard input is redirected.";
        internal const string StandardOutputEncodingNotAllowed = "StandardOutputEncoding is only supported when standard output is redirected.";
        internal const string NotSupportedRemoteThread = "Feature is not supported for threads on remote computers.";
        internal const string WaitReasonUnavailable = "WaitReason is only available if the ThreadState is Wait.";
        internal const string ProcessHandleNotSupported = "Process handles are not available on this NetWasm platform.";
        internal const string Argument_NullCharInEnvVar = "Environment variable cannot contain a null character.";
    }
}
