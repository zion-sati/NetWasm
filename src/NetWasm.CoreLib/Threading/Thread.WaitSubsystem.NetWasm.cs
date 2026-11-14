// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The Thread members the wait subsystem uses, as upstream's Thread.Mono.cs declares them for WASI. One thread runs, and
// a wait that would block throws before the thread would enter the wait-sleep-join state (WaitableObject), so that
// state is not recorded: ThreadState stays Running.

namespace System.Threading;

public sealed partial class Thread
{
    private WaitSubsystem.ThreadWaitInfo? _waitInfo;

    // Default from coreclr (src/utilcode/yieldprocessornormalized.cpp), as upstream's Mono runtime has it.
    internal static int OptimalMaxSpinWaitsPerSpinIteration => 7;

    internal WaitSubsystem.ThreadWaitInfo WaitInfo => _waitInfo ??= new WaitSubsystem.ThreadWaitInfo(this);

    // There is no other thread to yield to.
    internal static void UninterruptibleSleep0()
    {
    }

#pragma warning disable CA1822 // upstream's members are instance members
    internal void SetWaitSleepJoinState()
    {
    }

    internal void ClearWaitSleepJoinState()
    {
    }
#pragma warning restore CA1822
}
