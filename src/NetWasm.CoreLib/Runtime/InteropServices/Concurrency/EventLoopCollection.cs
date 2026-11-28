// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices.Concurrency;

using WebAssembly;

// A WASI program's event loop gives its collector the time the program spends waiting, as NetWasm's JavaScript
// hosts do in a worker or Node.js (idle-collection.mjs, quiet-collection.mjs), with timers of the loop's own.
//
// Idle: once the program has run and the loop waits, a timer runs out a short delay; if the loop is still
// waiting then or has waited again, the runtime's idle entry collects when the heap has grown enough for a
// collection to be worth its while, so the collection the program was heading for does not happen during its
// next burst of work. Collecting at each wait instead would collect more often than the heap needs in a loop
// that waits often while busy.
//
// Quiet: a program that drops what it held without allocating leaves garbage no growth announces. Once the
// program has run and then has not run for a whole period, the runtime's quiet entry collects whatever the
// growth. One collection follows each stretch of activity, none follows a stretch the idle collection already
// cleaned up after, and the period grows while collections free little and is never shorter than a multiple of
// what the last one took.
//
// The loop's clock is the program's monotonic clock; the runtime gets none.
internal sealed class EventLoopCollection : IEventLoopCollection
{
    private const ulong IdleDelayNanoseconds = 200_000_000;
    private const ulong ShortestQuietPeriodNanoseconds = 1_000_000_000;
    private const ulong LongestQuietPeriodNanoseconds = 60_000_000_000;
    // A collection takes at most one part in this many of the quiet time before the next.
    private const ulong RestPerCollection = 20;
    // A collection paid when it freed an eighth of what the heap occupied; the runtime answers in 255ths.
    private const int PaidShare = 32;

    private readonly IEventLoopTimers _timers;
    private readonly IMonotonicClock _clock;
    private readonly IRuntimeIdleCollector _runtime;
    private readonly Action _idle;
    private readonly Action _quiet;
    private bool _ran;
    private IPlatformSchedule? _idleTimer;
    private IPlatformSchedule? _quietTimer;
    private bool _ranSinceQuietTimer;
    private bool _ranSinceCollection;
    private ulong _quietPeriod = ShortestQuietPeriodNanoseconds;

    internal EventLoopCollection(IEventLoopTimers timers, IMonotonicClock clock, IRuntimeIdleCollector runtime)
    {
        _timers = timers ?? throw new ArgumentNullException();
        _clock = clock ?? throw new ArgumentNullException();
        _runtime = runtime ?? throw new ArgumentNullException();
        _idle = Idle;
        _quiet = Quiet;
    }

    public void ProgramRan() => _ran = true;

    public void LoopWaiting()
    {
        if (!_ran)
        {
            return;
        }

        _ran = false;
        _ranSinceCollection = true;
        _idleTimer ??= _timers.ScheduleBackground(_idle, IdleDelayNanoseconds);
        if (_quietTimer != null)
        {
            _ranSinceQuietTimer = true;
            return;
        }

        _quietTimer = _timers.ScheduleBackground(_quiet, _quietPeriod);
    }

    public void LoopEnded()
    {
        _idleTimer?.Cancel();
        _idleTimer = null;
        _quietTimer?.Cancel();
        _quietTimer = null;
        _ranSinceQuietTimer = false;
    }

    private void Idle()
    {
        _idleTimer = null;
        if (_runtime.CollectWhenIdle())
        {
            _ranSinceCollection = false;
        }
    }

    private void Quiet()
    {
        _quietTimer = null;
        if (_ranSinceQuietTimer)
        {
            _ranSinceQuietTimer = false;
            _quietTimer = _timers.ScheduleBackground(_quiet, _quietPeriod);
            return;
        }

        if (!_ranSinceCollection)
        {
            return;
        }

        _ranSinceCollection = false;
        var started = _clock.GetTimestamp();
        var answer = _runtime.CollectWhenQuiet();
        if (answer == 0)
        {
            return;
        }

        var rest = unchecked((ulong)(_clock.GetTimestamp() - started)) * RestPerCollection;
        var next = answer - 1 >= PaidShare ? ShortestQuietPeriodNanoseconds : _quietPeriod * 2;
        _quietPeriod = Math.Min(LongestQuietPeriodNanoseconds, Math.Max(next, rest));
    }
}

// The runtime's entries, which the compiler calls for GCCollectionRuntime's.
internal sealed class RuntimeIdleCollector : IRuntimeIdleCollector
{
    public bool CollectWhenIdle() => GCCollectionRuntime.CollectWhenIdle();

    public int CollectWhenQuiet() => GCCollectionRuntime.CollectWhenQuiet();
}
