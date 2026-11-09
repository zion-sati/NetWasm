namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum RangeBranchFoldingPolicy
{
    Disabled,
    // A branch the ranges of its operands settle jumps or falls through, and the code it cuts off goes.
    ProvenRanges,
}
