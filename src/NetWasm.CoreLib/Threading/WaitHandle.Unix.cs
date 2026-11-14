// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using Microsoft.Win32.SafeHandles;

namespace System.Threading
{
    public abstract partial class WaitHandle
    {
        private static int WaitOneCore(IntPtr handle, int millisecondsTimeout, bool useTrivialWaits) =>
            WaitSubsystem.Wait(handle, millisecondsTimeout, interruptible: !useTrivialWaits);

        private static int WaitMultipleIgnoringSyncContextCore(ReadOnlySpan<IntPtr> handles, bool waitAll, int millisecondsTimeout) =>
            WaitSubsystem.Wait(handles, waitAll, millisecondsTimeout);

        private static int SignalAndWaitCore(IntPtr handleToSignal, IntPtr handleToWaitOn, int millisecondsTimeout) =>
            WaitSubsystem.SignalAndWait(handleToSignal, handleToWaitOn, millisecondsTimeout);
    }
}
