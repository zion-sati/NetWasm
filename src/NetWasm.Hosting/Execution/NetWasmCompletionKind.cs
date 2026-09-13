namespace NetWasm.Hosting.Execution;

/// <summary>The infrastructure-level completion of one NetWasm execution.</summary>
public enum NetWasmCompletionKind
{
    Normal,
    ManagedFailure,
    ManagedCancellation,
    CallerCancellation,
    ContractFailure,
    HostFailure,
}
