using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed record WasmOptimizationSettings(
    FinalWasmOptimization BackendPreset,
    WasmInlineAnnotationSelection ManagedInlineAnnotations,
    WasmInlineAnnotationSelection RuntimeInlineAnnotations,
    LoopInvariantFieldHoistingPolicy LoopInvariantFieldHoisting =
        LoopInvariantFieldHoistingPolicy.Disabled,
    StaticDataViewEliminationPolicy StaticDataViewElimination =
        StaticDataViewEliminationPolicy.Disabled,
    AddressTakenValuePromotionPolicy AddressTakenValuePromotion =
        AddressTakenValuePromotionPolicy.Disabled,
    ManagedMethodInliningPolicy ManagedMethodInlining =
        ManagedMethodInliningPolicy.Disabled,
    BranchPrefixDuplicationPolicy BranchPrefixDuplication =
        BranchPrefixDuplicationPolicy.Disabled,
    ScalarCallPreservationPolicy ScalarCallPreservation =
        ScalarCallPreservationPolicy.Disabled,
    ArrayBoundsCheckEliminationPolicy ArrayBoundsCheckElimination =
        ArrayBoundsCheckEliminationPolicy.Disabled,
    LoopUnrollingPolicy LoopUnrolling = LoopUnrollingPolicy.Disabled,
    ConstantArgumentPropagationPolicy ConstantArgumentPropagation = ConstantArgumentPropagationPolicy.Disabled,
    StaticArrayPopulationEliminationPolicy StaticArrayPopulationElimination = StaticArrayPopulationEliminationPolicy.Disabled,
    ConstantCallFoldingPolicy ConstantCallFolding = ConstantCallFoldingPolicy.Disabled,
    SwitchBitTestPolicy SwitchBitTests = SwitchBitTestPolicy.Disabled,
    DevirtualizationPolicy Devirtualization = DevirtualizationPolicy.Disabled,
    AllocationEliminationPolicy AllocationElimination = AllocationEliminationPolicy.Disabled,
    LoopRotationPolicy LoopRotation = LoopRotationPolicy.Disabled,
    ArrayStoreCheckEliminationPolicy ArrayStoreCheckElimination =
        ArrayStoreCheckEliminationPolicy.Disabled,
    StaticInitializationHoistingPolicy StaticInitializationHoisting =
        StaticInitializationHoistingPolicy.Disabled,
    SmallBlockOperationPolicy SmallBlockOperations = SmallBlockOperationPolicy.Bulk,
    DispatcherEliminationPolicy DispatcherElimination = DispatcherEliminationPolicy.Disabled,
    EmptyFinallyRemovalPolicy EmptyFinallyRemoval = EmptyFinallyRemovalPolicy.Disabled,
    StructScalarReplacementPolicy StructScalarReplacement = StructScalarReplacementPolicy.Disabled,
    AllocationScalarReplacementPolicy AllocationScalarReplacement = AllocationScalarReplacementPolicy.Disabled,
    IdempotentLoopCollapsePolicy IdempotentLoopCollapse = IdempotentLoopCollapsePolicy.Disabled,
    LoopStoreForwardingPolicy LoopStoreForwarding = LoopStoreForwardingPolicy.Disabled,
    ArrayLoopIdiomPolicy ArrayLoopIdioms = ArrayLoopIdiomPolicy.Disabled,
    LoopVectorizationPolicy LoopVectorization = LoopVectorizationPolicy.Disabled);
