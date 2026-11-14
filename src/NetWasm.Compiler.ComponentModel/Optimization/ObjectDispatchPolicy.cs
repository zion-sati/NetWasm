namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ObjectDispatchPolicy
{
    // Each virtual call on object dispatches itself.
    Inline,
    // An unbounded virtual call on object with many targets calls CoreLib's shared body for it, which carries the
    // one copy of the dispatch over every allocated type: much less code, one more call.
    Shared,
}
