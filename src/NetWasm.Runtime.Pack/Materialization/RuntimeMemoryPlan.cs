namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeMemoryPlan(
    string Target,
    int PointerSizeBytes,
    long WasmPageSize,
    long Alignment,
    long ApplicationStaticDataEnd,
    long RuntimeGlobalBase,
    long InitialHeapSizeBytes,
    long MaximumMemorySizeBytes,
    long NativeStackSizeBytes);
