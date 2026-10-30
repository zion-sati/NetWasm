namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum SmallBlockOperationPolicy
{
    Bulk,
    // Copies and fills of up to 64 bytes are emitted inline rather than as
    // memory.copy and memory.fill, which the host runs out of line.
    Inline,
}
