namespace System.Runtime.InteropServices
{
    using Threading.Tasks;

    // An asynchronous Main is a callback-lifted wasi:cli/run. Start runs Main's ready work and
    // returns the callback code: EXIT once the task has completed, else WAIT on the event loop's
    // waitable set. The host then calls Callback with each event of that set until EXIT. A
    // completed task is observed with await semantics, so a fault or a cancellation surfaces as
    // a synchronous Main's would, and its exit code is kept for the adapter's task.return.
    internal static class AsyncEntryPoint
    {
        private const int CallbackExit = 0;
        private static Task? s_task;
        private static int s_exitCode;

        internal static int Start(Task task)
        {
            s_task = task ?? throw new ArgumentNullException();
            return Finish(PlatformServices.StartEntryPoint(task));
        }

        internal static int Callback(int eventCode, int waitable, int payload)
        {
            if (s_task == null)
            {
                throw new InvalidOperationException();
            }

            return Finish(PlatformServices.DeliverEntryPointEvent(
                s_task, eventCode, waitable, unchecked((uint)payload)));
        }

        // Main's exit code once Start or Callback returned EXIT: a Task<int>'s result, or
        // Environment.ExitCode for a Task. A non-zero one has already ended the process.
        internal static int ExitCode() => s_exitCode;

        private static int Finish(int code)
        {
            if (code == CallbackExit)
            {
                if (s_task is Task<int> withExitCode)
                {
                    s_exitCode = withExitCode.GetAwaiter().GetResult();
                }
                else
                {
                    s_task!.GetAwaiter().GetResult();
                    s_exitCode = Environment.ExitCode;
                }

                PlatformServices.RunProcessExit(s_exitCode);
            }

            return code;
        }
    }
}
