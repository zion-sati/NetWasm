// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices
{
    using Concurrency;

    // How long the event loop runs ready work in one turn of its host before it returns to the host.
    internal interface ICommandWorldBatch
    {
        // The host has called into the loop: a turn begins.
        void Begin();

        // Whether the loop may take another work item in this turn.
        bool Continues();
    }

    // A host with no event loop of its own to return to, such as a WASI command's: the loop runs its ready work
    // until none is left.
    internal sealed class UnboundedBatch : ICommandWorldBatch
    {
        internal static readonly UnboundedBatch Instance = new();

        public void Begin()
        {
        }

        public bool Continues() => true;
    }

    // A JavaScript host runs its own event loop. As .NET's single-threaded browser thread pool dispatches queued work
    // for at most ThreadPoolWorkQueue.DispatchQuantumMs (30 ms, dotnet/runtime 11.0.0,
    // src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs) before it returns to the
    // browser, the loop takes work until 30 ms of the turn have passed on the monotonic clock, then lets the host take
    // a turn of its own (render, handle input, run its timers) and continues in the next.
    internal sealed class QuantumBatch(IWasiMonotonicNowInvoker clock) : ICommandWorldBatch
    {
        private const ulong QuantumNanoseconds = 30_000_000;

        private readonly IWasiMonotonicNowInvoker _clock = clock ?? throw new ArgumentNullException();
        private ulong _start;

        public void Begin() => _start = _clock.Now();

        public bool Continues() => _clock.Now() - _start < QuantumNanoseconds;
    }
}
