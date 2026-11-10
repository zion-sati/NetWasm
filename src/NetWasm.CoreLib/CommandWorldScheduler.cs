namespace System.Runtime.InteropServices
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Concurrency;

    // The event loop of a command. An asynchronous Main is a callback-lifted wasi:cli/run: its
    // start and each callback run the ready work, then report EXIT once Main's task has completed
    // or WAIT on the loop's waitable set, and the host delivers that set's events. A delay is an
    // async-lowered wasi:clocks/monotonic-clock wait-for subtask; its work becomes ready when the
    // subtask returns.
    internal sealed class CommandWorldScheduler : IPlatformScheduler
    {
        private const int Returned = 2;
        private const int EventNone = 0;
        private const int CallbackExit = 0;
        private const int CallbackWait = 2;
        private const int SubtaskEvent = 1;
        private const int LastCopyEvent = 5;

        private readonly Queue<CommandWorldWork> _ready = new();
        // Pending work by its waitable, so an event finds its work however much is pending.
        private readonly Dictionary<int, CommandWorldWork> _waiting = new();
        private readonly IWasiMonotonicWaitInvoker _clock;
        private readonly IWasiWaitableSetCreateInvoker _createSet;
        private readonly IWasiWaitableJoinInvoker _join;
        private readonly IWasiSubtaskCancelInvoker _cancel;
        private readonly IWasiSubtaskDropInvoker _drop;
        private int _set;

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

        internal int PendingCount => _ready.Count + _waiting.Count;

        // Whether a JavaScript host runs the loop and can still complete a task through a
        // JavaScript promise the program awaits, with no subtask or copy pending.
        internal bool HostCanComplete { get; init; }

        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            return delayNanoseconds == 0
                ? Track(continuation, Returned)
                : Track(continuation, _clock.WaitFor(delayNanoseconds));
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
        internal int AwaitPending() => _waiting.Count == 0 ? CallbackExit : CallbackWait | (_set << 4);

        private int Report(Task task)
        {
            if (task.IsCompleted)
            {
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

            return CallbackWait | (_set << 4);
        }

        // Runs every work item that is ready, including work that ready work schedules: a callback
        // must not report WAIT while work is ready, since no event would wake it.
        private void RunReady()
        {
            while (_ready.Count != 0)
            {
                var work = _ready.Dequeue();
                if (work.TryStart())
                {
                    work.Continuation();
                }
            }
        }

        // Cancelling a delay cancels and drops its subtask; cancelling a copy only stops waiting
        // for it, since the stream or future's owner cancels or drops the copy.
        internal void Cancel(CommandWorldWork work)
        {
            // Work that became ready no longer waits, and its waitable may name newer work.
            if (work.Waitable != CommandWorldWork.NoWaitable &&
                _waiting.TryGetValue(work.Waitable, out var waiting) && waiting == work)
            {
                _waiting.Remove(work.Waitable);
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
            if (!_waiting.TryGetValue(waitable, out var work) || eventCode < SubtaskEvent ||
                eventCode > LastCopyEvent || work.IsCopy != (eventCode != SubtaskEvent))
            {
                throw new InvalidOperationException(
                    "The event loop received an event for no scheduled work.");
            }

            if (!work.IsCopy && payload != Returned)
            {
                return;
            }

            _waiting.Remove(waitable);
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

        private sealed class UnpumpedWork : IPlatformSchedule
        {
            public void Dispose()
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
            bool copy = false)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException();
            Continuation = continuation ?? throw new ArgumentNullException();
            Waitable = waitable;
            IsCopy = copy;
        }

        internal Action Continuation { get; }

        internal int Waitable { get; }

        // A stream or future end's copy, whose event carries the copy's result.
        internal bool IsCopy { get; }

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

        public void Dispose()
        {
            if (_active)
            {
                _active = false;
                _scheduler.Cancel(this);
            }
        }
    }
}
