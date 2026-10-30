namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ManagedMethodInliningPolicy
{
    Disabled,
    // Inlines small callees as .NET's JIT does; trades code size for speed.
    Speed,
    // Inlines only callees marked AggressiveInlining, as they ask.
    Aggressive,
    // Aggressive, plus field accessors, which inlining only shrinks, and the small, leaf
    // and loop-body callees of calls inside loops: the calls that run repeatedly. Most
    // of Speed's gain at a fraction of its code growth.
    Loops,
}
