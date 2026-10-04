namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkedMemoryLayout(
    string Target,
    long RuntimeGlobalBase,
    long DataEnd,
    long StackLow,
    long StackHigh,
    long HeapBase,
    long InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);
