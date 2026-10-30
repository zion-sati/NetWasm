namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum IdempotentLoopCollapsePolicy
{
    Disabled,
    // A counted loop that runs at least once and whose body repeats its first
    // iteration, such as one that fills or copies the same stack struct on
    // every pass, runs its body once.
    CountedLoops,
}
