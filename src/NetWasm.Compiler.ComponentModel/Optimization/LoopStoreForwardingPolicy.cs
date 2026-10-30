namespace NetWasm.Compiler.ComponentModel.Optimization;

public enum LoopStoreForwardingPolicy
{
    Disabled,
    // A loop that updates one array or span element in place, and writes
    // memory no other way, reads the value it last stored from a local.
    Elements,
}
