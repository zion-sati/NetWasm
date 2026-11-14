// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's half of LowLevelMonitor, in place of upstream's LowLevelMonitor.Unix.cs, which wraps a pthread mutex and
// condition. One thread runs, so acquiring never contends and nothing can signal a waiter. The wait subsystem throws
// before a wait that would block (WaitableObject), so a wait reaching here throws as upstream's single-threaded
// runtimes do where they would block.

using System.Runtime.CompilerServices;

namespace System.Threading
{
    internal partial struct LowLevelMonitor
    {
#pragma warning disable CA1822 // upstream's members are instance members
        public void Initialize()
        {
        }

        private void DisposeCore()
        {
        }

        private void AcquireCore()
        {
        }

        private void ReleaseCore()
        {
        }

        private void WaitCore() => RuntimeFeature.ThrowIfMultithreadingIsNotSupported();

        private bool WaitCore(int timeoutMilliseconds)
        {
            RuntimeFeature.ThrowIfMultithreadingIsNotSupported();
            return false;
        }

        private void Signal_ReleaseCore()
        {
        }
#pragma warning restore CA1822
    }
}
