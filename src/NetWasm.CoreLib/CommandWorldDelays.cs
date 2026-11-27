// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices
{
    using Concurrency;

    // The program's delays, in one queue by deadline, with one wasi:clocks wait-for subtask armed for the earliest, as
    // .NET's TimerQueue keeps one native timer. A delay that ends no sooner than the armed wait calls nothing on the host,
    // and a cancelled one leaves the queue at once; the armed wait stays until the loop next returns to its host with no
    // delay left (Disarm), so a program that sets and cancels a timeout on every operation (SemaphoreSlim.WaitAsync with
    // a timeout, Task.WaitAsync) starts and cancels no subtask per operation. When the armed wait returns, the delays
    // whose deadlines have passed become ready, in deadline order, and the wait is armed again for the next.
    // What a command's loop does before it returns to its host.
    internal interface ICommandWorldTurnHook
    {
        void BeforeHostTurn();
    }

    internal sealed class CommandWorldDelays : ICommandWorldTurnHook
    {
        private readonly CommandWorldScheduler _scheduler;
        private readonly IWasiMonotonicNowInvoker _now;
        private readonly IWasiMonotonicWaitInvoker _clock;
        // A binary min-heap by deadline, then by the order the delays were set; each delay knows its index.
        private CommandWorldDelay[] _heap = new CommandWorldDelay[4];
        private int _count;
        private readonly Action _fire;
        private CommandWorldWork? _armed;
        private ulong _armedDeadline;
        private ulong _sequence;

        internal CommandWorldDelays(CommandWorldScheduler scheduler, IWasiMonotonicNowInvoker now,
            IWasiMonotonicWaitInvoker clock)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException();
            _now = now ?? throw new ArgumentNullException();
            _clock = clock ?? throw new ArgumentNullException();
            _fire = Fire;
        }

        // The delays still to run, and whether a wait is armed for them (1) or not (0).
        internal int Count => _count;

        internal int Armed => _armed is null ? 0 : 1;

        internal IPlatformSchedule Add(Action continuation, ulong delayNanoseconds)
        {
            var now = _now.Now();
            var deadline = ulong.MaxValue - now < delayNanoseconds ? ulong.MaxValue : now + delayNanoseconds;
            var work = new CommandWorldDelay(this, continuation, deadline, _sequence++);
            Push(work);
            if (_armed is null || deadline < _armedDeadline)
            {
                Arm(deadline, now);
            }

            return work;
        }

        // A cancelled delay leaves the queue; the armed wait stays until Disarm.
        internal void Remove(CommandWorldDelay work)
        {
            if (work.HeapIndex >= 0)
            {
                RemoveAt(work.HeapIndex);
            }
        }

        // Before the loop returns to its host: a wait armed for delays since cancelled goes, so the loop neither waits
        // for it nor leaves it behind.
        public void BeforeHostTurn() => Disarm();

        internal void Disarm()
        {
            if (_count == 0 && _armed is { } armed)
            {
                _armed = null;
                armed.Dispose();
            }
        }

        // The armed wait has returned: the delays due by now become ready, and the wait is armed for the next.
        private void Fire()
        {
            _armed = null;
            var now = _now.Now();
            while (_count != 0 && _heap[0].Deadline <= now)
            {
                var due = _heap[0];
                RemoveAt(0);
                due.Post(_scheduler);
            }

            if (_count != 0)
            {
                Arm(_heap[0].Deadline, now);
            }
        }

        private void Arm(ulong deadline, ulong now)
        {
            if (_armed is { } previous)
            {
                _armed = null;
                previous.Dispose();
            }

            _armedDeadline = deadline;
            _armed = (CommandWorldWork)_scheduler.Track(_fire, _clock.WaitFor(deadline > now ? deadline - now : 0));
        }

        private static bool Before(CommandWorldDelay left, CommandWorldDelay right) =>
            left.Deadline < right.Deadline || left.Deadline == right.Deadline && left.Sequence < right.Sequence;

        private void Push(CommandWorldDelay work)
        {
            if (_count == _heap.Length)
            {
                Array.Resize(ref _heap, _count * 2);
            }

            _heap[_count] = work;
            work.HeapIndex = _count++;
            Up(work.HeapIndex);
        }

        private void RemoveAt(int index)
        {
            var removed = _heap[index];
            var last = --_count;
            if (index != last)
            {
                Place(_heap[last], index);
            }

            _heap[last] = null!;
            removed.HeapIndex = -1;
            if (index < _count)
            {
                Down(index);
                Up(index);
            }
        }

        private void Up(int index)
        {
            var work = _heap[index];
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (!Before(work, _heap[parent]))
                {
                    break;
                }

                Place(_heap[parent], index);
                index = parent;
            }

            Place(work, index);
        }

        private void Down(int index)
        {
            var work = _heap[index];
            while (true)
            {
                var child = 2 * index + 1;
                if (child >= _count)
                {
                    break;
                }

                if (child + 1 < _count && Before(_heap[child + 1], _heap[child]))
                {
                    child++;
                }

                if (!Before(_heap[child], work))
                {
                    break;
                }

                Place(_heap[child], index);
                index = child;
            }

            Place(work, index);
        }

        private void Place(CommandWorldDelay work, int index)
        {
            _heap[index] = work;
            work.HeapIndex = index;
        }
    }

    // A program's delay: its deadline on the monotonic clock, the order it was set in, and its place in the queue while
    // it waits (-1 once it has left). Once due it is posted as ready work; cancelling it takes it out of the queue, or
    // cancels that work.
    internal sealed class CommandWorldDelay : IPlatformSchedule
    {
        private readonly CommandWorldDelays _delays;
        private readonly Action _continuation;
        private IPlatformSchedule? _posted;
        private bool _active = true;

        internal CommandWorldDelay(CommandWorldDelays delays, Action continuation, ulong deadline, ulong sequence)
        {
            _delays = delays;
            _continuation = continuation ?? throw new ArgumentNullException();
            Deadline = deadline;
            Sequence = sequence;
        }

        internal ulong Deadline { get; }

        internal ulong Sequence { get; }

        internal int HeapIndex { get; set; } = -1;

        internal void Post(CommandWorldScheduler scheduler) => _posted = scheduler.Post(_continuation);

        public void Dispose()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            if (_posted is { } posted)
            {
                posted.Dispose();
            }
            else
            {
                _delays.Remove(this);
            }
        }
    }
}
