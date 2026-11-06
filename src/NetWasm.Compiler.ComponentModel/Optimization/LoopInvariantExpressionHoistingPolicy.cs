namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopInvariantExpressionHoistingPolicy
{
    Disabled,
    // A loop's invariant expression with a branch of its own is computed once ahead of the loop.
    BranchingExpressions,
}
