namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ConditionalStoreMergingPolicy
{
    Disabled,
    // Both arms of an if storing one field compute their values and store once where they meet.
    SameFieldArms,
}
