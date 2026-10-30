namespace NetWasm.Compiler.ComponentModel.Optimization;

public interface IManagedCompilerOptimizationOverridesParser
{
    WasmOptimizationOverrides? Parse(
        string? loopInvariantFieldHoisting,
        string? staticDataViewElimination,
        string? addressTakenValuePromotion,
        string? managedMethodInlining = null,
        string? branchPrefixDuplication = null,
        string? scalarCallPreservation = null,
        string? arrayBoundsCheckElimination = null,
        string? loopUnrolling = null,
        string? constantArgumentPropagation = null,
        string? staticArrayPopulationElimination = null,
        string? constantCallFolding = null,
        string? switchBitTests = null,
        string? devirtualization = null,
        string? allocationElimination = null,
        string? loopRotation = null,
        string? arrayStoreCheckElimination = null,
        string? dispatcherElimination = null,
        string? emptyFinallyRemoval = null,
        string? structScalarReplacement = null,
        string? allocationScalarReplacement = null,
        string? idempotentLoopCollapse = null,
        string? loopStoreForwarding = null,
        string? arrayLoopIdioms = null,
        string? loopVectorization = null);
}
