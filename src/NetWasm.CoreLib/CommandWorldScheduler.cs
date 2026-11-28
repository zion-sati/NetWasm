// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace System.Runtime.InteropServices
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Concurrency;

    // The event loop of a command. An asynchronous Main is a callback-lifted wasi:cli/run: its
    // start and each callback run the ready work, then report EXIT once Main's task has completed
    // or WAIT on the loop's waitable set, and the host delivers that set's events. The program's
    // delays wait in one queue behind one async-lowered wasi:clocks/monotonic-clock wait-for subtask
    // for the earliest (CommandWorldDelays); a delay's work becomes ready once its deadline has
    // passed. The loop's own timers (its collection's) wait beside the program's work, each its own
    // subtask, but never keep the loop waiting.
    internal sealed class CommandWorldScheduler : IPlatformScheduler, IEventLoopTimers
    {
        private const int Returned = 2;
        private const int EventNone = 0;
        private const int CallbackExit = 0;
        private const int CallbackYield = 1;
        private const int CallbackWait = 2;
        private const int SubtaskEvent = 1;
        private const int LastCopyEvent = 5;
        // Queued finalizers run every this many work items: soon after a collection queues them, as on .NET's
        // finalizer thread, without a call into the runtime after every item. The host's call into the program ends
        // with them too.
        private const int WorkBetweenFinalizers = 64;

        private readonly Queue<CommandWorldWork> _ready = new();
        // Pending work by its waitable, so an event finds its work however much is pending.
        private readonly Dictionary<int, CommandWorldWork> _waiting = new();
        // The loop's own pending timers, which the loop does not wait for.
        private readonly Dictionary<int, CommandWorldWork> _background = new();
        private IEventLoopCollection _collection = NoEventLoopCollection.Instance;
        private readonly IWasiMonotonicWaitInvoker _clock;
        private readonly IWasiWaitableSetCreateInvoker _createSet;
        private readonly IWasiWaitableJoinInvoker _join;
        private readonly IWasiSubtaskCancelInvoker _cancel;
        private readonly IWasiSubtaskDropInvoker _drop;
        // The program's delays, made with its first one, so a program that sets none carries no queue; and what the
        // loop does before it returns to its host, which the queue sets when it is made (CommandWorldDelays.Disarm).
        private CommandWorldDelays? _delays;
        private ICommandWorldTurnHook? _beforeHostTurn;
        private ICommandWorldBatch _batch = UnboundedBatch.Instance;
        private int _set;
        private int _workSinceFinalizers;

        internal CommandWorldScheduler(
            IWasiMonotonicWaitInvoker clock,
            IWasiWaitableSetCreateInvoker createSet,
            IWasiWaitableJoinInvoker join,
            IWasiSubtaskCancelInvoker cancel,
            IWasiSubtaskDropInvoker drop)
        {
            _clock = clock ?? throw new ArgumentNullException();
            _createSet = createSet ?? throw new ArgumentNullException();
            _join = join ?? throw new ArgumentNullException();
            _cancel = cancel ?? throw new ArgumentNullException();
            _drop = drop ?? throw new ArgumentNullException();
        }

        // What the loop does for its program's collector once it runs callbacks; until then, nothing.
        internal void CollectWith(IEventLoopCollection collection)
        {
            _collection = collection ?? throw new ArgumentNullException();
        }

        // The work still to run: ready work, what waits on the host (the delays' armed wait aside) and the delays.
        internal int PendingCount => _ready.Count + _waiting.Count - (_delays?.Armed ?? 0) + (_delays?.Count ?? 0);

        // Whether a JavaScript host runs the loop and can still complete a task through a
        // JavaScript promise the program awaits, with no subtask or copy pending.
        internal bool HostCanComplete { get; init; }

        // How long a turn runs ready work: 30 ms where a JavaScript host runs the loop, else until none is left.
        internal ICommandWorldBatch Batch
        {
            init => _batch = value ?? throw new ArgumentNullException();
        }

        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            return delayNanoseconds == 0
                ? Track(continuation, Returned)
                : Delays().Add(continuation, delayNanoseconds);
        }

        private CommandWorldDelays Delays()
        {
            if (_delays is null)
            {
                // The clock that waits also reads the time the deadlines are kept on.
                _delays = new CommandWorldDelays(this, (IWasiMonotonicNowInvoker)_clock, _clock);
                _beforeHostTurn = _delays;
            }

            return _delays;
        }

        public IPlatformSchedule Post(Action continuation)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            return Track(continuation, Returned);
        }

        // A timer of the loop's own: like a delay, but the loop ends without waiting for it, and
        // running it is not the program running. One that has run out already runs at the loop's
        // next turn.
        public IPlatformSchedule ScheduleBackground(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            var status = _clock.WaitFor(delayNanoseconds);
            if ((status & 0xF) == Returned)
            {
                var ready = new CommandWorldWork(this, continuation, CommandWorldWork.NoWaitable, background: true);
                _ready.Enqueue(ready);
                return ready;
            }

            var waiting = new CommandWorldWork(this, continuation, status >> 4, background: true);
            if (_set == 0)
            {
                _set = _createSet.Create();
            }

            _join.Join(waiting.Waitable, _set);
            _background.Add(waiting.Waitable, waiting);
            return waiting;
        }

        // An async-lowered call's status: its work is ready once the subtask has returned; a
        // subtask still starting or started joins the event loop's waitable set until it does.
        internal IPlatformSchedule Track(Action continuation, int status)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            if ((status & 0xF) != Returned)
            {
                var waiting = new CommandWorldWork(this, continuation, status >> 4);
                if (_set == 0)
                {
                    _set = _createSet.Create();
                }

                _join.Join(waiting.Waitable, _set);
                _waiting.Add(waiting.Waitable, waiting);
                return waiting;
            }

            var work = new CommandWorldWork(this, continuation, CommandWorldWork.NoWaitable);
            _ready.Enqueue(work);
            return work;
        }

        // A stream or future end with a copy in progress: it joins the waitable set until the
        // copy's event, whose payload (the count copied and how the copy ended) the continuation
        // receives. The end stays open.
        internal IPlatformSchedule TrackCopy(Action<uint> onCopied, int waitable)
        {
            if (onCopied == null)
            {
                throw new ArgumentNullException();
            }

            if (waitable == CommandWorldWork.NoWaitable)
            {
                throw new ArgumentOutOfRangeException();
            }

            CommandWorldWork? waiting = null;
            waiting = new CommandWorldWork(this, () => onCopied(waiting!.Payload), waitable, copy: true);
            if (_set == 0)
            {
                _set = _createSet.Create();
            }

            _join.Join(waitable, _set);
            _waiting.Add(waitable, waiting);
            return waiting;
        }

        // A callback-lifted export's start: runs the ready work, then reports EXIT once the task
        // has completed or WAIT on the scheduler's waitable set while its work is pending.
        internal int Start(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }

            // The program ran up to its first await.
            _collection.ProgramRan();
            RunReady();
            return Report(task);
        }

        // A callback-lifted export's callback: the host delivers one event of the waitable set, or
        // no event when it gives the loop a turn to run work a call into the program queued.
        internal int Deliver(Task task, int eventCode, int waitable, uint payload)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }

            if (eventCode != EventNone)
            {
                Ready(eventCode, waitable, payload);
            }

            RunReady();
            return Report(task);
        }

        // A JavaScript module's event loop, which its host runs after each call into the module:
        // runs the ready work, then reports WAIT on the waitable set while work is pending, else
        // EXIT.
        internal int RunHost()
        {
            RunReady();
            return AwaitPending();
        }

        internal int DeliverHost(int eventCode, int waitable, uint payload)
        {
            if (eventCode != EventNone)
            {
                Ready(eventCode, waitable, payload);
            }

            return RunHost();
        }

        // A task that has returned its result but still has copies or subtasks pending waits for
        // them: WAIT on the waitable set while any is pending, else EXIT.
        internal int AwaitPending()
        {
            _beforeHostTurn?.BeforeHostTurn();
            if (_ready.Count != 0)
            {
                return YieldToHost();
            }

            return _waiting.Count == 0 ? CallbackExit : CallbackWait | (_set << 4);
        }

        // The turn's batch ended with work still ready: the loop reports YIELD, with its waitable set if it has one,
        // and the host calls back after a turn of its own event loop, with an event of that set if one is ready, and
        // the loop continues its queue.
        private int YieldToHost() => CallbackYield | (_set << 4);

        private int Report(Task task)
        {
            _beforeHostTurn?.BeforeHostTurn();
            // Work the batch left ready runs before the loop ends, as it would have in one unbounded turn.
            if (_ready.Count != 0)
            {
                return YieldToHost();
            }

            if (task.IsCompleted)
            {
                _collection.LoopEnded();
                return CallbackExit;
            }

            if (_waiting.Count == 0)
            {
                if (!HostCanComplete)
                {
                    throw new InvalidOperationException(
                        "The asynchronous entry point cannot complete: no scheduled work or pending I/O remains.");
                }

                if (_set == 0)
                {
                    _set = _createSet.Create();
                }
            }

            _collection.LoopWaiting();
            return CallbackWait | (_set << 4);
        }

        // Runs the ready work, including work that ready work schedules, until none is left or the turn's batch ends;
        // the loop then reports YIELD, never WAIT, while work is ready, since no event would wake it. As upstream's
        // dispatch, the batch is tested after each item, so every turn runs at least one: a host that stalls the
        // module past the quantum before its first item still gets progress.
        private void RunReady()
        {
            _batch.Begin();
            var first = true;
            while (_ready.Count != 0 && (first || _batch.Continues()))
            {
                first = false;
                var work = _ready.Dequeue();
                if (work.TryStart())
                {
                    if (!work.IsBackground)
                    {
                        _collection.ProgramRan();
                    }

                    work.Continuation();
                }

                if (++_workSinceFinalizers == WorkBetweenFinalizers)
                {
                    _workSinceFinalizers = 0;
                    GCFinalizerRuntime.RunQueuedBetweenWork();
                }
            }
        }

        // Cancelling a subtask's work cancels and drops the subtask; cancelling a copy only stops
        // waiting for it, since the stream or future's owner cancels or drops the copy.
        internal void Cancel(CommandWorldWork work)
        {
            // Work that became ready no longer waits, and its waitable may name newer work.
            var pending = work.IsBackground ? _background : _waiting;
            if (work.Waitable != CommandWorldWork.NoWaitable &&
                pending.TryGetValue(work.Waitable, out var waiting) && waiting == work)
            {
                pending.Remove(work.Waitable);
                _join.Join(work.Waitable, 0);
                if (!work.IsCopy)
                {
                    _cancel.Cancel(work.Waitable);
                    _drop.Drop(work.Waitable);
                }
            }
        }

        // Readies the work whose subtask returned (a subtask that has only started keeps waiting),
        // or whose stream or future copy ended, with the copy's result as its payload.
        private void Ready(int eventCode, int waitable, uint payload)
        {
            if (!_waiting.TryGetValue(waitable, out var work) && !_background.TryGetValue(waitable, out work) ||
                eventCode < SubtaskEvent || eventCode > LastCopyEvent ||
                work.IsCopy != (eventCode != SubtaskEvent))
            {
                throw new InvalidOperationException(
                    "The event loop received an event for no scheduled work.");
            }

            if (!work.IsCopy && payload != Returned)
            {
                return;
            }

            (work.IsBackground ? _background : _waiting).Remove(waitable);
            _join.Join(work.Waitable, 0);
            if (work.IsCopy)
            {
                work.Payload = payload;
            }
            else
            {
                _drop.Drop(work.Waitable);
            }

            _ready.Enqueue(work);
        }
    }

    // A world without the monotonic clock cannot wait for anything. As on Mono's single-threaded
    // WASI runtime with nothing pumping its loop, deferred work never runs; awaiting completed
    // work still does, and the program imports nothing for it.
    internal sealed class UnpumpedScheduler : IPlatformScheduler
    {
        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds) =>
            continuation == null ? throw new ArgumentNullException() : new UnpumpedWork();

        public IPlatformSchedule Post(Action continuation) =>
            continuation == null ? throw new ArgumentNullException() : new UnpumpedWork();

        private sealed class UnpumpedWork : IPlatformSchedule
        {
            public void Cancel()
            {
            }
        }
    }

    internal sealed class CommandWorldWork : IPlatformSchedule
    {
        internal const int NoWaitable = 0;

        private readonly CommandWorldScheduler _scheduler;
        private bool _active = true;

        internal CommandWorldWork(
            CommandWorldScheduler scheduler,
            Action continuation,
            int waitable,
            bool copy = false,
            bool background = false)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException();
            Continuation = continuation ?? throw new ArgumentNullException();
            Waitable = waitable;
            IsCopy = copy;
            IsBackground = background;
        }

        internal Action Continuation { get; }

        internal int Waitable { get; }

        // A stream or future end's copy, whose event carries the copy's result.
        internal bool IsCopy { get; }

        // A timer of the loop's own, which the loop does not wait for.
        internal bool IsBackground { get; }

        internal uint Payload { get; set; }

        // Work cancelled after it became ready stays queued but no longer runs.
        internal bool TryStart()
        {
            if (!_active)
            {
                return false;
            }

            _active = false;
            return true;
        }

        public void Cancel()
        {
            if (_active)
            {
                _active = false;
                _scheduler.Cancel(this);
            }
        }
    }
}
