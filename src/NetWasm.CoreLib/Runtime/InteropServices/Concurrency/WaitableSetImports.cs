namespace System.Runtime.InteropServices.Concurrency;

using System.Runtime.InteropServices.WebAssembly;

// The Component Model's waitable-set built-ins, which block the current task
// until a stream, future or subtask it waits on has an event.
internal static class WaitableSetImports
{
    [ComponentBuiltIn("$root", "[waitable-set-new]")]
    internal static extern int Create();

    [ComponentBuiltIn("$root", "[waitable-join]")]
    internal static extern void Join(int waitable, int set);

    [ComponentBuiltIn("$root", "[waitable-set-wait]")]
    internal static extern int Wait(int set, nuint eventAddress);

    [ComponentBuiltIn("$root", "[subtask-drop]")]
    internal static extern void DropSubtask(int subtask);

    // A synchronous subtask.cancel: blocks until the subtask resolves and
    // returns its final state.
    [ComponentBuiltIn("$root", "[subtask-cancel]")]
    internal static extern int CancelSubtask(int subtask);

    // wasi:clocks/monotonic-clock wait-for, async-lowered: returns its
    // subtask's status.
    [ComponentBuiltIn("wasi:clocks/monotonic-clock@0.3.2", "[async-lower]wait-for")]
    internal static extern int WaitFor(long nanoseconds);
}
