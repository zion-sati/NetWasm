namespace System.Runtime.InteropServices
{
    using Concurrency;
    using Threading.Tasks;

    // A callback-lifted export's task. Its start runs the export's ready work and returns the
    // callback code; each callback delivers one event of the event loop's waitable set. Once the
    // export's Task completes, the generated completion lowers its result into task.return, and
    // the task lives on while work it left is pending, such as a response body's copy or a
    // future's write, so their events still have a task to wake; it exits when none is.
    //
    // One export task runs at a time: the task holds backpressure until it exits. Its
    // continuations run only in its own callbacks, and the Component Model cannot yet wake a task
    // that waits while another task's callback completes its work (a stream or future read and
    // written in the same instance traps), so interleaved tasks could wait forever.
    internal sealed class ComponentExportTask
    {
        private const int CallbackExit = 0;

        private readonly Func<CommandWorldScheduler> _loop;
        private readonly Func<int> _pending;
        private readonly IComponentBackpressure _backpressure;
        private Task? _task;
        private Action<Task>? _complete;
        private bool _returned;

        // pending reports what the task still waits for once it has returned: WAIT on the event
        // loop's set, or EXIT.
        internal ComponentExportTask(
            Func<CommandWorldScheduler> loop,
            Func<int> pending,
            IComponentBackpressure backpressure)
        {
            _loop = loop ?? throw new ArgumentNullException();
            _pending = pending ?? throw new ArgumentNullException();
            _backpressure = backpressure ?? throw new ArgumentNullException();
        }

        internal int Start(Task task, Action<Task> complete)
        {
            if (task == null || complete == null)
            {
                throw new ArgumentNullException();
            }

            if (_task != null)
            {
                throw new InvalidOperationException();
            }

            _task = task;
            _complete = complete;
            _returned = false;
            _backpressure.Hold(true);
            return Finish(task.IsCompleted ? CallbackExit : _loop().Start(task));
        }

        internal int Deliver(int eventCode, int waitable, int payload)
        {
            if (_task == null)
            {
                throw new InvalidOperationException();
            }

            return Finish(_loop().Deliver(_task, eventCode, waitable, unchecked((uint)payload)));
        }

        private int Finish(int code)
        {
            if (code == CallbackExit && !_returned)
            {
                _returned = true;
                _complete!(_task!);
            }

            if (!_returned)
            {
                return code;
            }

            var pending = _pending();
            if (pending != CallbackExit)
            {
                return pending;
            }

            _task = null;
            _complete = null;
            _backpressure.Hold(false);
            return CallbackExit;
        }
    }
}
