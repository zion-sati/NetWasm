// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

// What an event loop does for its program's collector. The loop tells it when the program ran and when the loop
// is about to wait on its waitable set; the loop owns the timers, which never keep it running.
internal interface IEventLoopCollection
{
    // The loop ran work of the program's. Costs a flag.
    void ProgramRan();

    // The loop is about to wait for its program's events.
    void LoopWaiting();

    // The loop is done: its timers are cancelled.
    void LoopEnded();
}

// Timers an event loop runs for its own purposes: one waits beside the program's work and its continuation runs
// on the loop, but the loop ends without waiting for it.
internal interface IEventLoopTimers
{
    IPlatformSchedule ScheduleBackground(Action continuation, ulong delayNanoseconds);
}

// The runtime's collection entries, as an event loop asks for them.
internal interface IRuntimeIdleCollector
{
    // The collection the heap is heading for, when it has grown enough to be worth it. Whether it collected.
    bool CollectWhenIdle();

    // A collection whatever the heap has grown by: 0 when it did not collect, otherwise 1 plus the share of the
    // heap it freed, in 255ths.
    int CollectWhenQuiet();
}

// A loop whose host collects for it (NetWasm's JavaScript hosts), or that has no clock.
internal sealed class NoEventLoopCollection : IEventLoopCollection
{
    internal static readonly NoEventLoopCollection Instance = new();

    private NoEventLoopCollection()
    {
    }

    public void ProgramRan()
    {
    }

    public void LoopWaiting()
    {
    }

    public void LoopEnded()
    {
    }
}
