namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum MulticastDispatchPolicy
{
    // A delegate type's multicast function calls its invoke function for each entry.
    Shared,
    // The multicast function dispatches each entry itself, a second copy of the invoke
    // function's dispatch that saves a call per entry.
    Inline,
}
