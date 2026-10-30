namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum StaticInitializationHoistingPolicy
{
    Disabled,
    // A BeforeFieldInit type a method accesses on every path to a return is
    // checked once, at the method's entry.
    MethodEntry,
}
