namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum ManagedMethodInlining
{
    Auto,
    Disabled,
    Enabled,
    // Inlines inside loops and what the callee asks for (ManagedMethodInliningPolicy.Loops).
    Loops,
}
