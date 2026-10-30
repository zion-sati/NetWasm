namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopVectorizationPolicy
{
    Disabled,
    // A counted loop over arrays whose body only stores lane-wise expressions of
    // elements at the counter first runs four or two iterations at a time with
    // Vector128 operations, when every access is in bounds.
    CountedLoops,
}
