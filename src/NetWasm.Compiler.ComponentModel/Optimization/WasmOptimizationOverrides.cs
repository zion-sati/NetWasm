namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed record WasmOptimizationOverrides(
    ManagedInliningHints ManagedInliningHints = ManagedInliningHints.Auto,
    ManagedLoopInvariantFieldHoisting LoopInvariantFieldHoisting =
        ManagedLoopInvariantFieldHoisting.Auto,
    ManagedStaticDataViewElimination StaticDataViewElimination =
        ManagedStaticDataViewElimination.Auto,
    ManagedAddressTakenValuePromotion AddressTakenValuePromotion =
        ManagedAddressTakenValuePromotion.Auto,
    ManagedMethodInlining ManagedMethodInlining = ManagedMethodInlining.Auto,
    ManagedBranchPrefixDuplication BranchPrefixDuplication =
        ManagedBranchPrefixDuplication.Auto,
    ManagedScalarCallPreservation ScalarCallPreservation =
        ManagedScalarCallPreservation.Auto,
    ManagedArrayBoundsCheckElimination ArrayBoundsCheckElimination =
        ManagedArrayBoundsCheckElimination.Auto,
    ManagedLoopUnrolling LoopUnrolling = ManagedLoopUnrolling.Auto,
    ManagedConstantArgumentPropagation ConstantArgumentPropagation = ManagedConstantArgumentPropagation.Auto,
    ManagedStaticArrayPopulationElimination StaticArrayPopulationElimination = ManagedStaticArrayPopulationElimination.Auto,
    ManagedConstantCallFolding ConstantCallFolding = ManagedConstantCallFolding.Auto,
    ManagedSwitchBitTests SwitchBitTests = ManagedSwitchBitTests.Auto,
    ManagedDevirtualization Devirtualization = ManagedDevirtualization.Auto,
    ManagedAllocationElimination AllocationElimination = ManagedAllocationElimination.Auto,
    ManagedLoopRotation LoopRotation = ManagedLoopRotation.Auto,
    ManagedArrayStoreCheckElimination ArrayStoreCheckElimination =
        ManagedArrayStoreCheckElimination.Auto,
    ManagedDispatcherElimination DispatcherElimination =
        ManagedDispatcherElimination.Auto,
    ManagedEmptyFinallyRemoval EmptyFinallyRemoval =
        ManagedEmptyFinallyRemoval.Auto,
    ManagedStructScalarReplacement StructScalarReplacement =
        ManagedStructScalarReplacement.Auto,
    ManagedAllocationScalarReplacement AllocationScalarReplacement =
        ManagedAllocationScalarReplacement.Auto,
    ManagedIdempotentLoopCollapse IdempotentLoopCollapse =
        ManagedIdempotentLoopCollapse.Auto,
    ManagedLoopStoreForwarding LoopStoreForwarding =
        ManagedLoopStoreForwarding.Auto,
    ManagedArrayLoopIdioms ArrayLoopIdioms =
        ManagedArrayLoopIdioms.Auto,
    ManagedLoopVectorization LoopVectorization =
        ManagedLoopVectorization.Auto);
