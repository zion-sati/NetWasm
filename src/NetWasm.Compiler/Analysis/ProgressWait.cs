using System;
using System.Threading;

namespace NetWasm.Compiler.Analysis;

// Waits on a progress event until a condition holds. The event is reset before the condition is
// looked at again, so a completion between the first look and the reset is seen by the second look
// rather than lost: it set the event before the reset cleared it.
internal static class ProgressWait
{
    internal static void Until<TState>(ManualResetEventSlim progress, TState state, Func<TState, bool> condition)
    {
        while (!condition(state))
        {
            progress.Reset();
            Interlocked.MemoryBarrier();
            if (condition(state))
            {
                return;
            }
            progress.Wait();
        }
    }
}
