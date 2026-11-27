// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
    LoopVectorizationPolicy LoopVectorization = LoopVectorizationPolicy.Disabled,
    PureCallReusePolicy PureCallReuse = PureCallReusePolicy.Disabled,
    FieldLoadReusePolicy FieldLoadReuse = FieldLoadReusePolicy.Disabled,
    ConditionalStoreMergingPolicy ConditionalStoreMerging = ConditionalStoreMergingPolicy.Disabled,
    InlineArrayDataPolicy InlineArrayData = InlineArrayDataPolicy.Disabled,
    QuotientReusePolicy QuotientReuse = QuotientReusePolicy.Disabled,
    IntegerCheckEliminationPolicy IntegerCheckElimination = IntegerCheckEliminationPolicy.Disabled,
    // Whether wasm-opt repeats the preset until the module stops shrinking (O1 and above).
    bool Converge = false,
    LoopInvariantExpressionHoistingPolicy LoopInvariantExpressionHoisting = LoopInvariantExpressionHoistingPolicy.Disabled,
    MulticastDispatchPolicy MulticastDispatch = MulticastDispatchPolicy.Shared,
    // Whether wasm-opt may assume the first KiB of memory is unused and fold constant address
    // adds into load and store offsets (O1 and above).
    bool LowMemoryUnused = false,
    NewObjectNullCheckEliminationPolicy NewObjectNullCheckElimination = NewObjectNullCheckEliminationPolicy.Disabled,
    LoopStrengthReductionPolicy LoopStrengthReduction = LoopStrengthReductionPolicy.Disabled,
    RangeBranchFoldingPolicy RangeBranchFolding = RangeBranchFoldingPolicy.Disabled,
    UnwrittenStaticFieldFoldingPolicy UnwrittenStaticFieldFolding = UnwrittenStaticFieldFoldingPolicy.Disabled,
    ObjectDispatchPolicy ObjectDispatch = ObjectDispatchPolicy.SharedAtLargeSites,
    ConditionalSelectPolicy ConditionalSelects = ConditionalSelectPolicy.Disabled,
    LocalSlotSharingPolicy LocalSlotSharing = LocalSlotSharingPolicy.Disabled,
    RuntimeAsyncSuspensionPolicy RuntimeAsyncSuspension = RuntimeAsyncSuspensionPolicy.Local,
    ByRefNullCheckEliminationPolicy ByRefNullCheckElimination = ByRefNullCheckEliminationPolicy.Disabled,
    // How many instructions a finally's copies may take (its handler's instructions times its targets) for its normal
    // exits to run them in place of the handler (FinallyCloner); 0 copies none.
    int FinallyCloningBudget = 0,
    ReceiverFlowDispatchPolicy ReceiverFlowDispatch = ReceiverFlowDispatchPolicy.GenericVirtualCalls);
