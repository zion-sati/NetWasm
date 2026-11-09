namespace System.Runtime.InteropServices
{
    using System.Collections.Generic;
    using WebAssembly;

    // The scheduler of a plain core module, which has no event loop. Ready work runs as soon as it
    // is scheduled, on one queue: the first schedule outside a run drains it, so a continuation
    // scheduled by another runs after it rather than inside it, and all of it runs before the
    // export that started it returns. A delay is the program's ITimerScheduler binding, whose
    // default throws.
    internal sealed class CoreModuleScheduler(ITimerScheduler timers) : IPlatformScheduler
    {
        private readonly ITimerScheduler _timers = timers ?? throw new ArgumentNullException();
        private readonly Queue<CoreModuleWork> _ready = new();
        private bool _running;

        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            var work = new CoreModuleWork(continuation);
            if (delayNanoseconds != 0)
            {
                work.Timer = _timers.Schedule(
                    () => Ready(work),
                    TimeSpan.FromTicks(checked((long)(delayNanoseconds / 100))));
                return work;
            }

            Ready(work);
            return work;
        }

        private void Ready(CoreModuleWork work)
        {
            _ready.Enqueue(work);
            if (_running)
            {
                return;
            }

            _running = true;
            try
            {
                while (_ready.Count != 0)
                {
                    _ready.Dequeue().Run();
                }
            }
            finally
            {
                _running = false;
            }
        }
    }

    internal sealed class CoreModuleWork(Action continuation) : IPlatformSchedule
    {
        private bool _active = true;

        internal IDisposable? Timer { get; set; }

        internal void Run()
        {
            if (_active)
            {
                _active = false;
                continuation();
            }
        }

        public void Dispose()
        {
            _active = false;
            Timer?.Dispose();
        }
    }
}
