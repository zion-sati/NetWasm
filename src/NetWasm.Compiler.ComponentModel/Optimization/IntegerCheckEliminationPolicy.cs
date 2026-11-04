namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum IntegerCheckEliminationPolicy
{
    Disabled,
    // Division, checked arithmetic and checked conversion checks that the operands' ranges rule out are not emitted.
    ProvenRanges,
}
