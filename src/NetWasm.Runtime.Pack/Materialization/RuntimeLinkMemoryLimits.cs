namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkMemoryLimits(
    long RuntimeGlobalBase,
    long? InitialMemorySizeBytes,
    long MaximumMemorySizeBytes);
