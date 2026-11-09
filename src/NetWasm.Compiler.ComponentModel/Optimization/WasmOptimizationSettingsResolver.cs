using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed class WasmOptimizationSettingsResolver : IWasmOptimizationSettingsResolver
{
    // None and O0 run no optimizer, so nothing reads the hints: the link keeps none.
    private static readonly FrozenDictionary<FinalWasmOptimization, WasmInlineAnnotationSelection> Presets =
        new Dictionary<FinalWasmOptimization, WasmInlineAnnotationSelection>
        {
            [FinalWasmOptimization.None] = WasmInlineAnnotationSelection.Remove,
            [FinalWasmOptimization.O0] = WasmInlineAnnotationSelection.Remove,
            [FinalWasmOptimization.O1] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O2] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O3] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.Os] = WasmInlineAnnotationSelection.SizeAware,
            [FinalWasmOptimization.Oz] = WasmInlineAnnotationSelection.SizeAware,
        }.ToFrozenDictionary();

    // AggressiveInlining in managed code is honored wherever speed counts, Oz
    // included, and Os ignores it (owner, 2026-10-23). The runtime's own hints
    // keep the preset's choice above.
    private static readonly FrozenDictionary<FinalWasmOptimization, WasmInlineAnnotationSelection> ManagedHintPresets =
        new Dictionary<FinalWasmOptimization, WasmInlineAnnotationSelection>
        {
            [FinalWasmOptimization.None] = WasmInlineAnnotationSelection.Remove,
            [FinalWasmOptimization.O0] = WasmInlineAnnotationSelection.Remove,
            [FinalWasmOptimization.O1] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O2] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O3] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.Os] = WasmInlineAnnotationSelection.SizeAware,
            [FinalWasmOptimization.Oz] = WasmInlineAnnotationSelection.Preserve,
        }.ToFrozenDictionary();

    // Oz takes O3's field hoisting, static data-view elimination, address-taken
    // promotion and bounds-check elimination: each shrinks the module, and with
    // Oz's inlining inside loops they make DoomSharp 0.71 of Loops alone (owner,
    // 2026-10-28). Os has no managed inlining for them to work on: no faster,
    // and its Brotli size grows, so it keeps them off.
    private static readonly FrozenDictionary<FinalWasmOptimization, LoopInvariantFieldHoistingPolicy>
        FieldHoistingPresets =
        new Dictionary<FinalWasmOptimization, LoopInvariantFieldHoistingPolicy>
        {
            [FinalWasmOptimization.None] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O0] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O1] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O2] = LoopInvariantFieldHoistingPolicy.Conservative,
            [FinalWasmOptimization.O3] = LoopInvariantFieldHoistingPolicy.NullChecks,
            [FinalWasmOptimization.Os] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.Oz] = LoopInvariantFieldHoistingPolicy.NullChecks,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ManagedInliningHints, WasmInlineAnnotationSelection> Hints =
        new Dictionary<ManagedInliningHints, WasmInlineAnnotationSelection>
        {
            [ManagedInliningHints.IgnoreAggressive] = WasmInlineAnnotationSelection.SizeAware,
            [ManagedInliningHints.Preserve] = WasmInlineAnnotationSelection.Preserve,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        StaticDataViewEliminationPolicy> StaticDataViewPresets =
        new Dictionary<FinalWasmOptimization, StaticDataViewEliminationPolicy>
        {
            [FinalWasmOptimization.None] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O0] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O1] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O2] = StaticDataViewEliminationPolicy.Conservative,
            [FinalWasmOptimization.O3] = StaticDataViewEliminationPolicy.Conservative,
            [FinalWasmOptimization.Os] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.Oz] = StaticDataViewEliminationPolicy.Conservative,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        AddressTakenValuePromotionPolicy> AddressTakenValuePromotionPresets =
        new Dictionary<FinalWasmOptimization, AddressTakenValuePromotionPolicy>
        {
            [FinalWasmOptimization.None] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O0] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O1] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O2] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O3] = AddressTakenValuePromotionPolicy.References,
            [FinalWasmOptimization.Os] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.Oz] = AddressTakenValuePromotionPolicy.References,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        ManagedMethodInliningPolicy> ManagedMethodInliningPresets =
        new Dictionary<FinalWasmOptimization, ManagedMethodInliningPolicy>
        {
            [FinalWasmOptimization.None] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.O0] = ManagedMethodInliningPolicy.Disabled,
            // As the managed hints above: AggressiveInlining callees inline at
            // O1, O2 and Oz; O2 and Oz also inline inside loops (micro table
            // geomean 0.904 at O2 for +2.3% size; DoomSharp at Oz 0.88 alone,
            // 0.62 with Oz's shrinking flags above), and O3 inlines what pays
            // for speed anywhere.
            [FinalWasmOptimization.O1] = ManagedMethodInliningPolicy.Aggressive,
            [FinalWasmOptimization.O2] = ManagedMethodInliningPolicy.Loops,
            [FinalWasmOptimization.O3] = ManagedMethodInliningPolicy.Speed,
            [FinalWasmOptimization.Os] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.Oz] = ManagedMethodInliningPolicy.Loops,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        BranchPrefixDuplicationPolicy> BranchPrefixDuplicationPresets =
        new Dictionary<FinalWasmOptimization, BranchPrefixDuplicationPolicy>
        {
            [FinalWasmOptimization.None] = BranchPrefixDuplicationPolicy.Disabled,
            [FinalWasmOptimization.O0] = BranchPrefixDuplicationPolicy.Disabled,
            [FinalWasmOptimization.O1] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.O2] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.O3] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.Os] = BranchPrefixDuplicationPolicy.SizeAware,
            [FinalWasmOptimization.Oz] = BranchPrefixDuplicationPolicy.SizeAware,
        }.ToFrozenDictionary();

    public WasmOptimizationSettings Resolve(
        FinalWasmOptimization preset,
        WasmOptimizationOverrides? options = null)
    {
        if (!Presets.TryGetValue(preset, out var presetSelection) ||
            !FieldHoistingPresets.TryGetValue(preset, out var fieldHoisting) ||
            !StaticDataViewPresets.TryGetValue(preset, out var staticDataViews) ||
            !AddressTakenValuePromotionPresets.TryGetValue(
                preset,
                out var addressTakenValuePromotion) ||
            !ManagedMethodInliningPresets.TryGetValue(
                preset,
                out var managedMethodInlining) ||
            !BranchPrefixDuplicationPresets.TryGetValue(
                preset,
                out var branchPrefixDuplication))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }
        var hint = options?.ManagedInliningHints ?? ManagedInliningHints.Auto;
        var managedSelection = ManagedHintPresets[preset];
        if (hint != ManagedInliningHints.Auto &&
            !Hints.TryGetValue(hint, out managedSelection))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var fieldOverride = options?.LoopInvariantFieldHoisting ??
            ManagedLoopInvariantFieldHoisting.Auto;
        fieldHoisting = fieldOverride switch
        {
            ManagedLoopInvariantFieldHoisting.Auto => fieldHoisting,
            ManagedLoopInvariantFieldHoisting.Disabled =>
                LoopInvariantFieldHoistingPolicy.Disabled,
            ManagedLoopInvariantFieldHoisting.Enabled =>
                LoopInvariantFieldHoistingPolicy.Conservative,
            ManagedLoopInvariantFieldHoisting.NullChecks =>
                LoopInvariantFieldHoistingPolicy.NullChecks,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var staticDataOverride = options?.StaticDataViewElimination ??
            ManagedStaticDataViewElimination.Auto;
        staticDataViews = staticDataOverride switch
        {
            ManagedStaticDataViewElimination.Auto => staticDataViews,
            ManagedStaticDataViewElimination.Disabled =>
                StaticDataViewEliminationPolicy.Disabled,
            ManagedStaticDataViewElimination.Enabled =>
                StaticDataViewEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var valuePromotionOverride = options?.AddressTakenValuePromotion ??
            ManagedAddressTakenValuePromotion.Auto;
        addressTakenValuePromotion = valuePromotionOverride switch
        {
            ManagedAddressTakenValuePromotion.Auto => addressTakenValuePromotion,
            ManagedAddressTakenValuePromotion.Disabled =>
                AddressTakenValuePromotionPolicy.Disabled,
            ManagedAddressTakenValuePromotion.Enabled =>
                AddressTakenValuePromotionPolicy.Conservative,
            ManagedAddressTakenValuePromotion.References =>
                AddressTakenValuePromotionPolicy.References,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var methodInliningOverride = options?.ManagedMethodInlining ??
            ManagedMethodInlining.Auto;
        managedMethodInlining = methodInliningOverride switch
        {
            ManagedMethodInlining.Auto => managedMethodInlining,
            ManagedMethodInlining.Disabled => ManagedMethodInliningPolicy.Disabled,
            ManagedMethodInlining.Enabled => ManagedMethodInliningPolicy.Speed,
            ManagedMethodInlining.Loops => ManagedMethodInliningPolicy.Loops,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var branchPrefixDuplicationOverride =
            options?.BranchPrefixDuplication ??
            ManagedBranchPrefixDuplication.Auto;
        branchPrefixDuplication = branchPrefixDuplicationOverride switch
        {
            ManagedBranchPrefixDuplication.Auto => branchPrefixDuplication,
            ManagedBranchPrefixDuplication.Disabled =>
                BranchPrefixDuplicationPolicy.Disabled,
            ManagedBranchPrefixDuplication.Enabled =>
                BranchPrefixDuplicationPolicy.Speed,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var scalarOverride = options?.ScalarCallPreservation ?? ManagedScalarCallPreservation.Auto;
        var scalarCalls = scalarOverride switch
        {
            ManagedScalarCallPreservation.Auto => preset == FinalWasmOptimization.O3
                ? ScalarCallPreservationPolicy.Conservative : ScalarCallPreservationPolicy.Disabled,
            ManagedScalarCallPreservation.Disabled => ScalarCallPreservationPolicy.Disabled,
            ManagedScalarCallPreservation.Enabled => ScalarCallPreservationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var arrayBoundsOverride = options?.ArrayBoundsCheckElimination ?? ManagedArrayBoundsCheckElimination.Auto;
        var arrayBounds = arrayBoundsOverride switch
        {
            ManagedArrayBoundsCheckElimination.Auto => preset is FinalWasmOptimization.O3 or FinalWasmOptimization.Oz
                ? ArrayBoundsCheckEliminationPolicy.Conservative : ArrayBoundsCheckEliminationPolicy.Disabled,
            ManagedArrayBoundsCheckElimination.Disabled => ArrayBoundsCheckEliminationPolicy.Disabled,
            ManagedArrayBoundsCheckElimination.Enabled => ArrayBoundsCheckEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var unrollingOverride = options?.LoopUnrolling ?? ManagedLoopUnrolling.Auto;
        var unrolling = unrollingOverride switch
        {
            ManagedLoopUnrolling.Auto => preset == FinalWasmOptimization.O3
                ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
            ManagedLoopUnrolling.Disabled => LoopUnrollingPolicy.Disabled,
            ManagedLoopUnrolling.Enabled => LoopUnrollingPolicy.Bounded,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var wholeProgramDefaults = preset is FinalWasmOptimization.O2 or FinalWasmOptimization.O3 or
            FinalWasmOptimization.Os or FinalWasmOptimization.Oz;
        var constantOverride = options?.ConstantArgumentPropagation ?? ManagedConstantArgumentPropagation.Auto;
        var constantArguments = constantOverride switch
        {
            ManagedConstantArgumentPropagation.Auto => wholeProgramDefaults
                ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            ManagedConstantArgumentPropagation.Disabled => ConstantArgumentPropagationPolicy.Disabled,
            ManagedConstantArgumentPropagation.Enabled => ConstantArgumentPropagationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var populationOverride = options?.StaticArrayPopulationElimination ?? ManagedStaticArrayPopulationElimination.Auto;
        var staticPopulation = populationOverride switch
        {
            ManagedStaticArrayPopulationElimination.Auto => wholeProgramDefaults
                ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
            ManagedStaticArrayPopulationElimination.Disabled => StaticArrayPopulationEliminationPolicy.Disabled,
            ManagedStaticArrayPopulationElimination.Enabled => StaticArrayPopulationEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Folding removes calls and the code their results decide, so it suits size and speed presets alike.
        var callFoldingOverride = options?.ConstantCallFolding ?? ManagedConstantCallFolding.Auto;
        var callFolding = callFoldingOverride switch
        {
            ManagedConstantCallFolding.Auto => wholeProgramDefaults
                ? ConstantCallFoldingPolicy.Conservative : ConstantCallFoldingPolicy.Disabled,
            ManagedConstantCallFolding.Disabled => ConstantCallFoldingPolicy.Disabled,
            ManagedConstantCallFolding.Enabled => ConstantCallFoldingPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A bit test replaces a compare per case, so it is smaller as well as faster.
        var bitTestOverride = options?.SwitchBitTests ?? ManagedSwitchBitTests.Auto;
        var bitTests = bitTestOverride switch
        {
            ManagedSwitchBitTests.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? SwitchBitTestPolicy.Disabled : SwitchBitTestPolicy.Conservative,
            ManagedSwitchBitTests.Disabled => SwitchBitTestPolicy.Disabled,
            ManagedSwitchBitTests.Enabled => SwitchBitTestPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A direct call replaces a dispatch, so it is smaller as well as faster.
        var devirtualizationOverride = options?.Devirtualization ?? ManagedDevirtualization.Auto;
        var devirtualization = devirtualizationOverride switch
        {
            ManagedDevirtualization.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? DevirtualizationPolicy.Disabled : DevirtualizationPolicy.ExactType,
            ManagedDevirtualization.Disabled => DevirtualizationPolicy.Disabled,
            ManagedDevirtualization.Enabled => DevirtualizationPolicy.ExactType,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Removing an allocation nothing reads removes code as well as work.
        var allocationOverride = options?.AllocationElimination ?? ManagedAllocationElimination.Auto;
        var allocations = allocationOverride switch
        {
            ManagedAllocationElimination.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? AllocationEliminationPolicy.Disabled : AllocationEliminationPolicy.Unused,
            ManagedAllocationElimination.Disabled => AllocationEliminationPolicy.Disabled,
            ManagedAllocationElimination.Enabled => AllocationEliminationPolicy.Unused,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Rotation repeats the loop condition, so Os keeps one copy. Oz rotates
        // conditions without calls: once field hoisting drops the null check, an
        // unrotated loop takes two branches per iteration (owner, 2026-10-29).
        var rotationOverride = options?.LoopRotation ?? ManagedLoopRotation.Auto;
        var rotation = rotationOverride switch
        {
            ManagedLoopRotation.Auto => preset switch
            {
                FinalWasmOptimization.O3 => LoopRotationPolicy.Guarded,
                FinalWasmOptimization.Oz => LoopRotationPolicy.CallFree,
                _ => LoopRotationPolicy.Disabled,
            },
            ManagedLoopRotation.Disabled => LoopRotationPolicy.Disabled,
            ManagedLoopRotation.Enabled => LoopRotationPolicy.Guarded,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Removing a provably redundant check only shrinks code, so every optimizing preset keeps it.
        var storeCheckOverride = options?.ArrayStoreCheckElimination ?? ManagedArrayStoreCheckElimination.Auto;
        var storeChecks = storeCheckOverride switch
        {
            ManagedArrayStoreCheckElimination.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? ArrayStoreCheckEliminationPolicy.Disabled : ArrayStoreCheckEliminationPolicy.ExactElementTypes,
            ManagedArrayStoreCheckElimination.Disabled => ArrayStoreCheckEliminationPolicy.Disabled,
            ManagedArrayStoreCheckElimination.Enabled => ArrayStoreCheckEliminationPolicy.ExactElementTypes,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Labeled structure is smaller and faster than a dispatcher, so every
        // optimizing preset takes it.
        var dispatcherOverride = options?.DispatcherElimination ?? ManagedDispatcherElimination.Auto;
        var dispatchers = dispatcherOverride switch
        {
            ManagedDispatcherElimination.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? DispatcherEliminationPolicy.Disabled : DispatcherEliminationPolicy.Reducible,
            ManagedDispatcherElimination.Disabled => DispatcherEliminationPolicy.Disabled,
            ManagedDispatcherElimination.Enabled => DispatcherEliminationPolicy.Reducible,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A removed handler is less code and frees the try's body from root
        // publication, so every optimizing preset takes it.
        var emptyFinallyOverride = options?.EmptyFinallyRemoval ?? ManagedEmptyFinallyRemoval.Auto;
        var emptyFinallys = emptyFinallyOverride switch
        {
            ManagedEmptyFinallyRemoval.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? EmptyFinallyRemovalPolicy.Disabled : EmptyFinallyRemovalPolicy.EmptyHandlers,
            ManagedEmptyFinallyRemoval.Disabled => EmptyFinallyRemovalPolicy.Disabled,
            ManagedEmptyFinallyRemoval.Enabled => EmptyFinallyRemovalPolicy.EmptyHandlers,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Field locals are less code than a value-frame struct and stay in
        // registers, so every optimizing preset takes them.
        var structOverride = options?.StructScalarReplacement ?? ManagedStructScalarReplacement.Auto;
        var structs = structOverride switch
        {
            ManagedStructScalarReplacement.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? StructScalarReplacementPolicy.Disabled : StructScalarReplacementPolicy.FieldAccessedLocals,
            ManagedStructScalarReplacement.Disabled => StructScalarReplacementPolicy.Disabled,
            ManagedStructScalarReplacement.Enabled => StructScalarReplacementPolicy.FieldAccessedLocals,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Field locals replace an allocation and its memory accesses with
        // fewer instructions, so every optimizing preset takes them.
        var allocationScalarOverride = options?.AllocationScalarReplacement ?? ManagedAllocationScalarReplacement.Auto;
        var allocationScalars = allocationScalarOverride switch
        {
            ManagedAllocationScalarReplacement.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? AllocationScalarReplacementPolicy.Disabled : AllocationScalarReplacementPolicy.NonEscapingObjects,
            ManagedAllocationScalarReplacement.Disabled => AllocationScalarReplacementPolicy.Disabled,
            ManagedAllocationScalarReplacement.Enabled => AllocationScalarReplacementPolicy.NonEscapingObjects,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A collapsed loop is less code and runs its body once, so every
        // optimizing preset takes it.
        var idempotentLoopOverride = options?.IdempotentLoopCollapse ?? ManagedIdempotentLoopCollapse.Auto;
        var idempotentLoops = idempotentLoopOverride switch
        {
            ManagedIdempotentLoopCollapse.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? IdempotentLoopCollapsePolicy.Disabled : IdempotentLoopCollapsePolicy.CountedLoops,
            ManagedIdempotentLoopCollapse.Disabled => IdempotentLoopCollapsePolicy.Disabled,
            ManagedIdempotentLoopCollapse.Enabled => IdempotentLoopCollapsePolicy.CountedLoops,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A forwarded element leaves a loop's dependency chain without adding
        // code, so every optimizing preset forwards.
        var loopStoreOverride = options?.LoopStoreForwarding ?? ManagedLoopStoreForwarding.Auto;
        var loopStores = loopStoreOverride switch
        {
            ManagedLoopStoreForwarding.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? LoopStoreForwardingPolicy.Disabled : LoopStoreForwardingPolicy.Elements,
            ManagedLoopStoreForwarding.Disabled => LoopStoreForwardingPolicy.Disabled,
            ManagedLoopStoreForwarding.Enabled => LoopStoreForwardingPolicy.Elements,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A bulk fill, copy or fold is more code than the loop it runs ahead of, so
        // only the speed presets take it.
        var arrayLoopOverride = options?.ArrayLoopIdioms ?? ManagedArrayLoopIdioms.Auto;
        var arrayLoops = arrayLoopOverride switch
        {
            ManagedArrayLoopIdioms.Auto => preset is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or FinalWasmOptimization.O3
                ? ArrayLoopIdiomPolicy.Bulk : ArrayLoopIdiomPolicy.Disabled,
            ManagedArrayLoopIdioms.Disabled => ArrayLoopIdiomPolicy.Disabled,
            ManagedArrayLoopIdioms.Enabled => ArrayLoopIdiomPolicy.Bulk,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A vectorized loop runs ahead of a copy of the scalar loop, so only the
        // O2 and O3 presets take it.
        var vectorizationOverride = options?.LoopVectorization ?? ManagedLoopVectorization.Auto;
        var vectorization = vectorizationOverride switch
        {
            ManagedLoopVectorization.Auto => preset is FinalWasmOptimization.O2 or FinalWasmOptimization.O3
                ? LoopVectorizationPolicy.CountedLoops : LoopVectorizationPolicy.Disabled,
            ManagedLoopVectorization.Disabled => LoopVectorizationPolicy.Disabled,
            ManagedLoopVectorization.Enabled => LoopVectorizationPolicy.CountedLoops,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A reused result is one call less and no more code, so every
        // optimizing preset takes it.
        var pureCallOverride = options?.PureCallReuse ?? ManagedPureCallReuse.Auto;
        var pureCalls = pureCallOverride switch
        {
            ManagedPureCallReuse.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? PureCallReusePolicy.Disabled : PureCallReusePolicy.RepeatedArguments,
            ManagedPureCallReuse.Disabled => PureCallReusePolicy.Disabled,
            ManagedPureCallReuse.Enabled => PureCallReusePolicy.RepeatedArguments,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var fieldLoadsOverride = options?.FieldLoadReuse ?? ManagedFieldLoadReuse.Auto;
        var fieldLoads = fieldLoadsOverride switch
        {
            ManagedFieldLoadReuse.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? FieldLoadReusePolicy.Disabled : FieldLoadReusePolicy.AvailableLoads,
            ManagedFieldLoadReuse.Disabled => FieldLoadReusePolicy.Disabled,
            ManagedFieldLoadReuse.Enabled => FieldLoadReusePolicy.AvailableLoads,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var storeMergingOverride = options?.ConditionalStoreMerging ?? ManagedConditionalStoreMerging.Auto;
        var storeMerging = storeMergingOverride switch
        {
            ManagedConditionalStoreMerging.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? ConditionalStoreMergingPolicy.Disabled : ConditionalStoreMergingPolicy.SameFieldArms,
            ManagedConditionalStoreMerging.Disabled => ConditionalStoreMergingPolicy.Disabled,
            ManagedConditionalStoreMerging.Enabled => ConditionalStoreMergingPolicy.SameFieldArms,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var arrayDataOverride = options?.InlineArrayData ?? ManagedInlineArrayData.Auto;
        var arrayData = arrayDataOverride switch
        {
            ManagedInlineArrayData.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? InlineArrayDataPolicy.Disabled : InlineArrayDataPolicy.Inline,
            ManagedInlineArrayData.Disabled => InlineArrayDataPolicy.Disabled,
            ManagedInlineArrayData.Enabled => InlineArrayDataPolicy.Inline,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var quotientsOverride = options?.QuotientReuse ?? ManagedQuotientReuse.Auto;
        var quotients = quotientsOverride switch
        {
            ManagedQuotientReuse.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? QuotientReusePolicy.Disabled : QuotientReusePolicy.AvailableQuotients,
            ManagedQuotientReuse.Disabled => QuotientReusePolicy.Disabled,
            ManagedQuotientReuse.Enabled => QuotientReusePolicy.AvailableQuotients,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var integerChecksOverride = options?.IntegerCheckElimination ?? ManagedIntegerCheckElimination.Auto;
        var integerChecks = integerChecksOverride switch
        {
            ManagedIntegerCheckElimination.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? IntegerCheckEliminationPolicy.Disabled : IntegerCheckEliminationPolicy.ProvenRanges,
            ManagedIntegerCheckElimination.Disabled => IntegerCheckEliminationPolicy.Disabled,
            ManagedIntegerCheckElimination.Enabled => IntegerCheckEliminationPolicy.ProvenRanges,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Moving a branching computation out of its loop makes every iteration
        // shorter and adds only the local that carries its value, so every
        // optimizing preset takes it.
        var invariantExpressionsOverride = options?.LoopInvariantExpressionHoisting ?? ManagedLoopInvariantExpressionHoisting.Auto;
        var invariantExpressions = invariantExpressionsOverride switch
        {
            ManagedLoopInvariantExpressionHoisting.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? LoopInvariantExpressionHoistingPolicy.Disabled : LoopInvariantExpressionHoistingPolicy.BranchingExpressions,
            ManagedLoopInvariantExpressionHoisting.Disabled => LoopInvariantExpressionHoistingPolicy.Disabled,
            ManagedLoopInvariantExpressionHoisting.Enabled => LoopInvariantExpressionHoistingPolicy.BranchingExpressions,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A second copy of each delegate type's dispatch saves multicast entries a call,
        // so only the speed presets take it.
        var multicastOverride = options?.MulticastInlineDispatch ?? ManagedMulticastInlineDispatch.Auto;
        var multicast = multicastOverride switch
        {
            ManagedMulticastInlineDispatch.Auto => preset is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or
                FinalWasmOptimization.O3
                ? MulticastDispatchPolicy.Inline : MulticastDispatchPolicy.Shared,
            ManagedMulticastInlineDispatch.Disabled => MulticastDispatchPolicy.Shared,
            ManagedMulticastInlineDispatch.Enabled => MulticastDispatchPolicy.Inline,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // Objects newobj produced are proven within each method. Proving a constructor's this
        // non-null takes one more pass over the whole program, so only the presets that
        // already pay for whole-program work take it: O3, and Os and Oz, where each null
        // check left out is also bytes saved.
        var newObjectsOverride = options?.NewObjectNullCheckElimination ?? ManagedNewObjectNullCheckElimination.Auto;
        var newObjects = newObjectsOverride switch
        {
            ManagedNewObjectNullCheckElimination.Auto => preset switch
            {
                FinalWasmOptimization.None or FinalWasmOptimization.O0 => NewObjectNullCheckEliminationPolicy.Disabled,
                FinalWasmOptimization.O1 or FinalWasmOptimization.O2 => NewObjectNullCheckEliminationPolicy.NewObjects,
                _ => NewObjectNullCheckEliminationPolicy.ConstructedObjects,
            },
            ManagedNewObjectNullCheckElimination.Disabled => NewObjectNullCheckEliminationPolicy.Disabled,
            ManagedNewObjectNullCheckElimination.Enabled => NewObjectNullCheckEliminationPolicy.ConstructedObjects,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A reference that moves with a loop's counter costs a local and an add per
        // iteration, and saves the counter's scaling and an add per read.
        var strengthReductionOverride = options?.LoopStrengthReduction ?? ManagedLoopStrengthReduction.Auto;
        var strengthReduction = strengthReductionOverride switch
        {
            ManagedLoopStrengthReduction.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? LoopStrengthReductionPolicy.Disabled : LoopStrengthReductionPolicy.AddressInduction,
            ManagedLoopStrengthReduction.Disabled => LoopStrengthReductionPolicy.Disabled,
            ManagedLoopStrengthReduction.Enabled => LoopStrengthReductionPolicy.AddressInduction,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        // A branch the ranges of its operands settle costs a range analysis of
        // the method, and saves the comparison and the code it cuts off.
        var rangeBranchesOverride = options?.RangeBranchFolding ?? ManagedRangeBranchFolding.Auto;
        var rangeBranches = rangeBranchesOverride switch
        {
            ManagedRangeBranchFolding.Auto => preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? RangeBranchFoldingPolicy.Disabled : RangeBranchFoldingPolicy.ProvenRanges,
            ManagedRangeBranchFolding.Disabled => RangeBranchFoldingPolicy.Disabled,
            ManagedRangeBranchFolding.Enabled => RangeBranchFoldingPolicy.ProvenRanges,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        if (preset == FinalWasmOptimization.None &&
            (hint != ManagedInliningHints.Auto ||
             fieldOverride is ManagedLoopInvariantFieldHoisting.Enabled or ManagedLoopInvariantFieldHoisting.NullChecks ||
             staticDataOverride == ManagedStaticDataViewElimination.Enabled ||
             valuePromotionOverride is ManagedAddressTakenValuePromotion.Enabled or ManagedAddressTakenValuePromotion.References ||
             methodInliningOverride is ManagedMethodInlining.Enabled or ManagedMethodInlining.Loops ||
             scalarOverride == ManagedScalarCallPreservation.Enabled ||
             arrayBoundsOverride == ManagedArrayBoundsCheckElimination.Enabled ||
             unrollingOverride == ManagedLoopUnrolling.Enabled ||
             constantOverride == ManagedConstantArgumentPropagation.Enabled ||
             populationOverride == ManagedStaticArrayPopulationElimination.Enabled ||
             callFoldingOverride == ManagedConstantCallFolding.Enabled ||
             bitTestOverride == ManagedSwitchBitTests.Enabled ||
             devirtualizationOverride == ManagedDevirtualization.Enabled ||
             allocationOverride == ManagedAllocationElimination.Enabled ||
             rotationOverride == ManagedLoopRotation.Enabled ||
             storeCheckOverride == ManagedArrayStoreCheckElimination.Enabled ||
             dispatcherOverride == ManagedDispatcherElimination.Enabled ||
             emptyFinallyOverride == ManagedEmptyFinallyRemoval.Enabled ||
             structOverride == ManagedStructScalarReplacement.Enabled ||
             allocationScalarOverride == ManagedAllocationScalarReplacement.Enabled ||
             idempotentLoopOverride == ManagedIdempotentLoopCollapse.Enabled ||
             loopStoreOverride == ManagedLoopStoreForwarding.Enabled ||
             arrayLoopOverride == ManagedArrayLoopIdioms.Enabled ||
             vectorizationOverride == ManagedLoopVectorization.Enabled ||
             pureCallOverride == ManagedPureCallReuse.Enabled ||
             fieldLoadsOverride == ManagedFieldLoadReuse.Enabled ||
             storeMergingOverride == ManagedConditionalStoreMerging.Enabled ||
             arrayDataOverride == ManagedInlineArrayData.Enabled ||
             quotientsOverride == ManagedQuotientReuse.Enabled ||
             integerChecksOverride == ManagedIntegerCheckElimination.Enabled ||
             invariantExpressionsOverride == ManagedLoopInvariantExpressionHoisting.Enabled ||
             multicastOverride == ManagedMulticastInlineDispatch.Enabled ||
             newObjectsOverride == ManagedNewObjectNullCheckElimination.Enabled ||
             strengthReductionOverride == ManagedLoopStrengthReduction.Enabled ||
             rangeBranchesOverride == ManagedRangeBranchFolding.Enabled ||
             branchPrefixDuplicationOverride ==
                ManagedBranchPrefixDuplication.Enabled))
        {
            throw new ArgumentException(
                "Managed optimization options require final optimization.", nameof(options));
        }

        return new(
            preset,
            managedSelection,
            presetSelection,
            fieldHoisting,
            staticDataViews,
            addressTakenValuePromotion,
            managedMethodInlining,
            branchPrefixDuplication,
            scalarCalls,
            arrayBounds,
            unrolling,
            constantArguments,
            staticPopulation,
            callFolding,
            bitTests,
            devirtualization,
            allocations,
            rotation,
            storeChecks,
            // Fewer checks run and fewer are emitted, so every optimizing preset hoists.
            preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? StaticInitializationHoistingPolicy.Disabled
                : StaticInitializationHoistingPolicy.MethodEntry,
            // Inline blocks cost code at every call site with a variable size,
            // so only the speed presets take them.
            preset is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or FinalWasmOptimization.O3
                ? SmallBlockOperationPolicy.Inline
                : SmallBlockOperationPolicy.Bulk,
            dispatchers,
            emptyFinallys,
            structs,
            allocationScalars,
            idempotentLoops,
            loopStores,
            arrayLoops,
            vectorization,
            pureCalls,
            fieldLoads,
            storeMerging,
            arrayData,
            quotients,
            integerChecks,
            options?.Converge ?? false,
            invariantExpressions,
            multicast,
            // Static data starts above the first KiB, so wasm-opt may fold constant address adds
            // into offsets, but no preset asks it to: Cranelift keeps a folded heap + index live
            // across calls and adds it with arm64's slower extended-register form, and both arm64
            // and x86-64 have more cases slower with it than without.
            options?.LowMemoryUnused ?? false,
            newObjects,
            strengthReduction,
            rangeBranches);
    }
}
