namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopStrengthReductionPolicy
{
    Disabled,
    // A loop's reads of a reference plus its counter read a reference that moves with the counter.
    AddressInduction,
}
