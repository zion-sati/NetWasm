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
        ManagedLoopVectorization.Auto,
    ManagedPureCallReuse PureCallReuse =
        ManagedPureCallReuse.Auto,
    ManagedFieldLoadReuse FieldLoadReuse =
        ManagedFieldLoadReuse.Auto,
    ManagedConditionalStoreMerging ConditionalStoreMerging =
        ManagedConditionalStoreMerging.Auto,
    ManagedInlineArrayData InlineArrayData =
        ManagedInlineArrayData.Auto,
    ManagedQuotientReuse QuotientReuse =
        ManagedQuotientReuse.Auto,
    ManagedIntegerCheckElimination IntegerCheckElimination =
        ManagedIntegerCheckElimination.Auto,
    // Repeats wasm-opt's preset until the module stops shrinking. Opt-in: on DoomSharp it
    // doubled the optimizer's time for 0.04-0.12% of size and no measured speed.
    bool Converge = false,
    ManagedLoopInvariantExpressionHoisting LoopInvariantExpressionHoisting =
        ManagedLoopInvariantExpressionHoisting.Auto,
    ManagedMulticastInlineDispatch MulticastInlineDispatch =
        ManagedMulticastInlineDispatch.Auto);
