namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum FieldLoadReusePolicy
{
    Disabled,
    // A field read again while nothing that could change it has run reads a local holding the first read.
    AvailableLoads,
}
