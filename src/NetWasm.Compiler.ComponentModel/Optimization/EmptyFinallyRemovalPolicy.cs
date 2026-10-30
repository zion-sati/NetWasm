namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum EmptyFinallyRemovalPolicy
{
    Disabled,
    // A finally or fault handler with nothing left to run once its calls are
    // inlined, such as the one around a foreach over a struct enumerator,
    // is removed with its protected region.
    EmptyHandlers,
}
