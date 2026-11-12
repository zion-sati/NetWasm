namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum UnwrittenStaticFieldFoldingPolicy
{
    Disabled,
    // A static field no reachable code stores to or takes the address of reads as its default value.
    WholeProgram,
}
