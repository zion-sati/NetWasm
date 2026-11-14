// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The strings System.Threading uses, from CoreLib's SR classes (dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce)
// or System.Threading's Strings.resx at that commit.

namespace System
{
    internal static partial class SR
    {
        internal const string HostExecutionContextManager_InvalidOperation_CannotOverrideSetWithoutRevert = "Must override both HostExecutionContextManager.SetHostExecutionContext and HostExecutionContextManager.Revert.";
        internal const string HostExecutionContextManager_InvalidOperation_CannotUseSwitcherOtherThread = "Undo operation must be performed on the thread where the corresponding context was Set.";
        internal const string HostExecutionContextManager_InvalidOperation_NotNewCaptureContext = "Cannot apply a context that has been marshaled across AppDomains, that was not acquired through a Capture operation or that has already been the argument to a Set call.";
        internal const string BarrierPostPhaseException = "The postPhaseAction failed with an exception.";
        internal const string Barrier_AddParticipants_Overflow_ArgumentOutOfRange = "Adding participantCount participants would result in the number of participants exceeding the maximum number allowed.";
        internal const string Barrier_InvalidOperation_CalledFromPHA = "This method may not be called from within the postPhaseAction.";
        internal const string Barrier_RemoveParticipants_ArgumentOutOfRange = "The participantCount argument must be less than or equal the number of participants.";
        internal const string Barrier_RemoveParticipants_InvalidOperation = "The participantCount argument is greater than the number of participants that haven't yet arrived at the barrier in this phase.";
        internal const string Barrier_SignalAndWait_ArgumentOutOfRange = "The specified timeout must represent a value between -1 and Int32.MaxValue, inclusive.";
        internal const string Barrier_SignalAndWait_InvalidOperation_ThreadsExceeded = "The number of threads using the barrier exceeded the total number of registered participants.";
        internal const string Barrier_SignalAndWait_InvalidOperation_ZeroTotal = "The barrier has no registered participants.";
        internal const string Common_OperationCanceled = "The operation was canceled.";
        internal const string CountdownEvent_Decrement_BelowZero = "Invalid attempt made to decrement the event's count below zero.";
        internal const string CountdownEvent_Increment_AlreadyMax = "The increment operation would cause the CurrentCount to overflow.";
        internal const string CountdownEvent_Increment_AlreadyZero = "The event is already signaled and cannot be incremented.";
        internal const string ExceptionFromHResult = "(Exception from HRESULT: 0x{0:X})";

        internal static string Format(string resourceFormat, object? p1) => string.Format(resourceFormat, p1);

        internal static string Format(string resourceFormat, object? p1, object? p2) => string.Format(resourceFormat, p1, p2);

        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3) => string.Format(resourceFormat, p1, p2, p3);

        internal static string Format(string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(resourceFormat, args);

        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args) =>
            args is null ? resourceFormat : string.Format(provider, resourceFormat, args);
        internal const string Overflow_UInt16 = "Value was either too large or too small for a UInt16.";
        internal const string ReaderWriterLock_InvalidLockCookie = "The specified lock cookie is invalid for this operation. {0}";
        internal const string ReaderWriterLock_NotOwner = "Attempt to release a lock that is not owned by the calling thread. {0}";
        internal const string ReaderWriterLock_RestoreLockWithOwnedLocks = "ReaderWriterLock.RestoreLock was called without releasing all locks acquired since the call to ReleaseLock.";
        internal const string ReaderWriterLock_Timeout = "The operation has timed out. {0}";
    }
}
