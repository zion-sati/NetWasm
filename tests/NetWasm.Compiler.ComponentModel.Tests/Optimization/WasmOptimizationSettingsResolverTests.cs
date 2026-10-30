using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Tests.Optimization;

public sealed class WasmOptimizationSettingsResolverTests
{
    // O3 rotates every conditional loop; Oz only those whose condition makes no call.
    private static LoopRotationPolicy LoopRotation(FinalWasmOptimization preset) => preset switch
    {
        FinalWasmOptimization.O3 => LoopRotationPolicy.Guarded,
        FinalWasmOptimization.Oz => LoopRotationPolicy.CallFree,
        _ => LoopRotationPolicy.Disabled,
    };

    private static StaticInitializationHoistingPolicy StaticInitializationHoisting(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? StaticInitializationHoistingPolicy.Disabled
            : StaticInitializationHoistingPolicy.MethodEntry;

    private static SmallBlockOperationPolicy SmallBlockOperations(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or FinalWasmOptimization.O3
            ? SmallBlockOperationPolicy.Inline
            : SmallBlockOperationPolicy.Bulk;

    private static DispatcherEliminationPolicy DispatcherElimination(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? DispatcherEliminationPolicy.Disabled
            : DispatcherEliminationPolicy.Reducible;

    private static EmptyFinallyRemovalPolicy EmptyFinallyRemoval(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? EmptyFinallyRemovalPolicy.Disabled
            : EmptyFinallyRemovalPolicy.EmptyHandlers;

    private static StructScalarReplacementPolicy StructScalarReplacement(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? StructScalarReplacementPolicy.Disabled
            : StructScalarReplacementPolicy.FieldAccessedLocals;

    private static AllocationScalarReplacementPolicy AllocationScalarReplacement(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? AllocationScalarReplacementPolicy.Disabled
            : AllocationScalarReplacementPolicy.NonEscapingObjects;

    private static IdempotentLoopCollapsePolicy IdempotentLoopCollapse(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? IdempotentLoopCollapsePolicy.Disabled
            : IdempotentLoopCollapsePolicy.CountedLoops;

    private static LoopStoreForwardingPolicy LoopStoreForwarding(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
            ? LoopStoreForwardingPolicy.Disabled
            : LoopStoreForwardingPolicy.Elements;

    private static ArrayLoopIdiomPolicy ArrayLoopIdioms(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or FinalWasmOptimization.O3
            ? ArrayLoopIdiomPolicy.Bulk
            : ArrayLoopIdiomPolicy.Disabled;

    private static LoopVectorizationPolicy LoopVectorization(FinalWasmOptimization preset) =>
        preset is FinalWasmOptimization.O2 or FinalWasmOptimization.O3
            ? LoopVectorizationPolicy.CountedLoops
            : LoopVectorizationPolicy.Disabled;

    private static bool WholeProgramDefault(FinalWasmOptimization preset) => preset switch
    {
        FinalWasmOptimization.None or FinalWasmOptimization.O0 or FinalWasmOptimization.O1 => false,
        FinalWasmOptimization.O2 or FinalWasmOptimization.O3 or FinalWasmOptimization.Os or FinalWasmOptimization.Oz => true,
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeProgramOverridesAreIndependentOfEachOtherAndThePreset(bool enabled)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>().Where(value => value != FinalWasmOptimization.None))
        {
            var baseline = resolver.Resolve(preset);
            var constants = resolver.Resolve(preset, new(ConstantArgumentPropagation: enabled
                ? ManagedConstantArgumentPropagation.Enabled : ManagedConstantArgumentPropagation.Disabled));
            var population = resolver.Resolve(preset, new(StaticArrayPopulationElimination: enabled
                ? ManagedStaticArrayPopulationElimination.Enabled : ManagedStaticArrayPopulationElimination.Disabled));
            var calls = resolver.Resolve(preset, new(ConstantCallFolding: enabled
                ? ManagedConstantCallFolding.Enabled : ManagedConstantCallFolding.Disabled));
            var bitTests = resolver.Resolve(preset, new(SwitchBitTests: enabled
                ? ManagedSwitchBitTests.Enabled : ManagedSwitchBitTests.Disabled));
            var devirtualization = resolver.Resolve(preset, new(Devirtualization: enabled
                ? ManagedDevirtualization.Enabled : ManagedDevirtualization.Disabled));
            var allocations = resolver.Resolve(preset, new(AllocationElimination: enabled
                ? ManagedAllocationElimination.Enabled : ManagedAllocationElimination.Disabled));
            var rotation = resolver.Resolve(preset, new(LoopRotation: enabled
                ? ManagedLoopRotation.Enabled : ManagedLoopRotation.Disabled));
            var storeChecks = resolver.Resolve(preset, new(ArrayStoreCheckElimination: enabled
                ? ManagedArrayStoreCheckElimination.Enabled : ManagedArrayStoreCheckElimination.Disabled));
            var dispatchers = resolver.Resolve(preset, new(DispatcherElimination: enabled
                ? ManagedDispatcherElimination.Enabled : ManagedDispatcherElimination.Disabled));
            var emptyFinallys = resolver.Resolve(preset, new(EmptyFinallyRemoval: enabled
                ? ManagedEmptyFinallyRemoval.Enabled : ManagedEmptyFinallyRemoval.Disabled));
            var structs = resolver.Resolve(preset, new(StructScalarReplacement: enabled
                ? ManagedStructScalarReplacement.Enabled : ManagedStructScalarReplacement.Disabled));
            var allocationScalars = resolver.Resolve(preset, new(AllocationScalarReplacement: enabled
                ? ManagedAllocationScalarReplacement.Enabled : ManagedAllocationScalarReplacement.Disabled));
            var idempotentLoops = resolver.Resolve(preset, new(IdempotentLoopCollapse: enabled
                ? ManagedIdempotentLoopCollapse.Enabled : ManagedIdempotentLoopCollapse.Disabled));
            var loopStores = resolver.Resolve(preset, new(LoopStoreForwarding: enabled
                ? ManagedLoopStoreForwarding.Enabled : ManagedLoopStoreForwarding.Disabled));
            var arrayLoops = resolver.Resolve(preset, new(ArrayLoopIdioms: enabled
                ? ManagedArrayLoopIdioms.Enabled : ManagedArrayLoopIdioms.Disabled));
            var vectorization = resolver.Resolve(preset, new(LoopVectorization: enabled
                ? ManagedLoopVectorization.Enabled : ManagedLoopVectorization.Disabled));
            Assert.Equal(baseline with
            {
                ConstantArgumentPropagation = enabled ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            }, constants);
            Assert.Equal(baseline with
            {
                StaticArrayPopulationElimination = enabled ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
            }, population);
            Assert.Equal(baseline with
            {
                ConstantCallFolding = enabled ? ConstantCallFoldingPolicy.Conservative : ConstantCallFoldingPolicy.Disabled,
            }, calls);
            Assert.Equal(baseline with
            {
                SwitchBitTests = enabled ? SwitchBitTestPolicy.Conservative : SwitchBitTestPolicy.Disabled,
            }, bitTests);
            Assert.Equal(baseline with
            {
                Devirtualization = enabled ? DevirtualizationPolicy.ExactType : DevirtualizationPolicy.Disabled,
            }, devirtualization);
            Assert.Equal(baseline with
            {
                AllocationElimination = enabled ? AllocationEliminationPolicy.Unused : AllocationEliminationPolicy.Disabled,
            }, allocations);
            Assert.Equal(baseline with
            {
                LoopRotation = enabled ? LoopRotationPolicy.Guarded : LoopRotationPolicy.Disabled,
            }, rotation);
            Assert.Equal(baseline with
            {
                ArrayStoreCheckElimination = enabled
                    ? ArrayStoreCheckEliminationPolicy.ExactElementTypes : ArrayStoreCheckEliminationPolicy.Disabled,
            }, storeChecks);
            Assert.Equal(baseline with
            {
                DispatcherElimination = enabled
                    ? DispatcherEliminationPolicy.Reducible : DispatcherEliminationPolicy.Disabled,
            }, dispatchers);
            Assert.Equal(baseline with
            {
                EmptyFinallyRemoval = enabled
                    ? EmptyFinallyRemovalPolicy.EmptyHandlers : EmptyFinallyRemovalPolicy.Disabled,
            }, emptyFinallys);
            Assert.Equal(baseline with
            {
                StructScalarReplacement = enabled
                    ? StructScalarReplacementPolicy.FieldAccessedLocals : StructScalarReplacementPolicy.Disabled,
            }, structs);
            Assert.Equal(baseline with
            {
                AllocationScalarReplacement = enabled
                    ? AllocationScalarReplacementPolicy.NonEscapingObjects : AllocationScalarReplacementPolicy.Disabled,
            }, allocationScalars);
            Assert.Equal(baseline with
            {
                IdempotentLoopCollapse = enabled
                    ? IdempotentLoopCollapsePolicy.CountedLoops : IdempotentLoopCollapsePolicy.Disabled,
            }, idempotentLoops);
            Assert.Equal(baseline with
            {
                LoopStoreForwarding = enabled
                    ? LoopStoreForwardingPolicy.Elements : LoopStoreForwardingPolicy.Disabled,
            }, loopStores);
            Assert.Equal(baseline with
            {
                ArrayLoopIdioms = enabled
                    ? ArrayLoopIdiomPolicy.Bulk : ArrayLoopIdiomPolicy.Disabled,
            }, arrayLoops);
            Assert.Equal(baseline with
            {
                LoopVectorization = enabled
                    ? LoopVectorizationPolicy.CountedLoops : LoopVectorizationPolicy.Disabled,
            }, vectorization);
        }
    }

    [Fact]
    public void WholeProgramOverridesRejectInvalidValuesAndEnabledNone()
    {
        var resolver = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(ConstantArgumentPropagation: (ManagedConstantArgumentPropagation)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(StaticArrayPopulationElimination: (ManagedStaticArrayPopulationElimination)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(ConstantCallFolding: (ManagedConstantCallFolding)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(SwitchBitTests: (ManagedSwitchBitTests)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(Devirtualization: (ManagedDevirtualization)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(AllocationElimination: (ManagedAllocationElimination)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(LoopRotation: (ManagedLoopRotation)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(ArrayStoreCheckElimination: (ManagedArrayStoreCheckElimination)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(DispatcherElimination: (ManagedDispatcherElimination)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(EmptyFinallyRemoval: (ManagedEmptyFinallyRemoval)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(StructScalarReplacement: (ManagedStructScalarReplacement)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(AllocationScalarReplacement: (ManagedAllocationScalarReplacement)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(IdempotentLoopCollapse: (ManagedIdempotentLoopCollapse)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(LoopStoreForwarding: (ManagedLoopStoreForwarding)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(ArrayLoopIdioms: (ManagedArrayLoopIdioms)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(LoopVectorization: (ManagedLoopVectorization)255)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(ConstantArgumentPropagation: ManagedConstantArgumentPropagation.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(StaticArrayPopulationElimination: ManagedStaticArrayPopulationElimination.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(ConstantCallFolding: ManagedConstantCallFolding.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(SwitchBitTests: ManagedSwitchBitTests.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(Devirtualization: ManagedDevirtualization.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(AllocationElimination: ManagedAllocationElimination.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(LoopRotation: ManagedLoopRotation.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(ArrayStoreCheckElimination: ManagedArrayStoreCheckElimination.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(DispatcherElimination: ManagedDispatcherElimination.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(EmptyFinallyRemoval: ManagedEmptyFinallyRemoval.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(StructScalarReplacement: ManagedStructScalarReplacement.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(AllocationScalarReplacement: ManagedAllocationScalarReplacement.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(IdempotentLoopCollapse: ManagedIdempotentLoopCollapse.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(LoopStoreForwarding: ManagedLoopStoreForwarding.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(ArrayLoopIdioms: ManagedArrayLoopIdioms.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(LoopVectorization: ManagedLoopVectorization.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(LoopInvariantFieldHoisting: ManagedLoopInvariantFieldHoisting.NullChecks)));
        Assert.Equal(resolver.Resolve(FinalWasmOptimization.None), resolver.Resolve(FinalWasmOptimization.None,
            new(ConstantArgumentPropagation: ManagedConstantArgumentPropagation.Disabled,
                StaticArrayPopulationElimination: ManagedStaticArrayPopulationElimination.Disabled,
                ConstantCallFolding: ManagedConstantCallFolding.Disabled,
                SwitchBitTests: ManagedSwitchBitTests.Disabled,
                Devirtualization: ManagedDevirtualization.Disabled,
                AllocationElimination: ManagedAllocationElimination.Disabled,
                LoopRotation: ManagedLoopRotation.Disabled,
                ArrayStoreCheckElimination: ManagedArrayStoreCheckElimination.Disabled,
                DispatcherElimination: ManagedDispatcherElimination.Disabled,
                EmptyFinallyRemoval: ManagedEmptyFinallyRemoval.Disabled,
                StructScalarReplacement: ManagedStructScalarReplacement.Disabled,
                AllocationScalarReplacement: ManagedAllocationScalarReplacement.Disabled,
                IdempotentLoopCollapse: ManagedIdempotentLoopCollapse.Disabled,
                LoopStoreForwarding: ManagedLoopStoreForwarding.Disabled,
                ArrayLoopIdioms: ManagedArrayLoopIdioms.Disabled,
                LoopVectorization: ManagedLoopVectorization.Disabled)));
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(FinalWasmOptimization.O0, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(FinalWasmOptimization.O1, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Aggressive,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.O2, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.Conservative, StaticDataViewEliminationPolicy.Conservative,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Loops,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.O3, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.NullChecks, StaticDataViewEliminationPolicy.Conservative,
        AddressTakenValuePromotionPolicy.References, ManagedMethodInliningPolicy.Speed,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.Os, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.SizeAware)]
    [InlineData(FinalWasmOptimization.Oz, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.NullChecks, StaticDataViewEliminationPolicy.Conservative,
        AddressTakenValuePromotionPolicy.References, ManagedMethodInliningPolicy.Loops,
        BranchPrefixDuplicationPolicy.SizeAware)]
    public void PreservesPresetDefaults(
        FinalWasmOptimization preset,
        WasmInlineAnnotationSelection selection,
        LoopInvariantFieldHoistingPolicy fieldHoisting,
        StaticDataViewEliminationPolicy staticDataViews,
        AddressTakenValuePromotionPolicy valuePromotion,
        ManagedMethodInliningPolicy methodInlining,
        BranchPrefixDuplicationPolicy branchPrefixDuplication)
    {
        var resolver = Create();
        // Managed AggressiveInlining hints also survive at Oz; the runtime's follow the preset.
        var managedSelection = preset == FinalWasmOptimization.Oz ? WasmInlineAnnotationSelection.Preserve : selection;
        var expected = new WasmOptimizationSettings(
            preset,
            managedSelection,
            selection,
            fieldHoisting,
            staticDataViews,
            valuePromotion,
            methodInlining,
            branchPrefixDuplication,
            preset == FinalWasmOptimization.O3
                ? ScalarCallPreservationPolicy.Conservative
                : ScalarCallPreservationPolicy.Disabled,
            preset is FinalWasmOptimization.O3 or FinalWasmOptimization.Oz
                ? ArrayBoundsCheckEliminationPolicy.Conservative
                : ArrayBoundsCheckEliminationPolicy.Disabled,
            preset == FinalWasmOptimization.O3 ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
            WholeProgramDefault(preset) ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            WholeProgramDefault(preset) ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
            WholeProgramDefault(preset) ? ConstantCallFoldingPolicy.Conservative : ConstantCallFoldingPolicy.Disabled,
            preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? SwitchBitTestPolicy.Disabled : SwitchBitTestPolicy.Conservative,
            preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? DevirtualizationPolicy.Disabled : DevirtualizationPolicy.ExactType,
            preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? AllocationEliminationPolicy.Disabled : AllocationEliminationPolicy.Unused,
            LoopRotation(preset),
            preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                ? ArrayStoreCheckEliminationPolicy.Disabled : ArrayStoreCheckEliminationPolicy.ExactElementTypes,
            StaticInitializationHoisting(preset),
            SmallBlockOperations(preset),
            DispatcherElimination(preset),
            EmptyFinallyRemoval(preset),
            StructScalarReplacement(preset),
            AllocationScalarReplacement(preset),
            IdempotentLoopCollapse(preset),
            LoopStoreForwarding(preset),
            ArrayLoopIdioms(preset),
            LoopVectorization(preset));
        var actual = resolver.Resolve(preset);
        Assert.Equal(expected, actual);
        Assert.Equal(preset, actual.BackendPreset);
        Assert.Equal(managedSelection, actual.ManagedInlineAnnotations);
        Assert.Equal(selection, actual.RuntimeInlineAnnotations);
        Assert.Equal(expected, resolver.Resolve(preset, new()));
    }

    [Theory]
    [InlineData(ManagedInliningHints.IgnoreAggressive, WasmInlineAnnotationSelection.SizeAware)]
    [InlineData(ManagedInliningHints.Preserve, WasmInlineAnnotationSelection.Preserve)]
    public void ExplicitHintsOverrideEveryEnabledPreset(
        ManagedInliningHints hint, WasmInlineAnnotationSelection selection)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>().Where(value => value != FinalWasmOptimization.None))
        {
            var runtimeSelection = preset is FinalWasmOptimization.O1 or
                FinalWasmOptimization.O2 or FinalWasmOptimization.O3
                ? WasmInlineAnnotationSelection.Preserve
                : WasmInlineAnnotationSelection.SizeAware;
            var fieldHoisting = preset switch
            {
                FinalWasmOptimization.O2 => LoopInvariantFieldHoistingPolicy.Conservative,
                FinalWasmOptimization.O3 or FinalWasmOptimization.Oz => LoopInvariantFieldHoistingPolicy.NullChecks,
                _ => LoopInvariantFieldHoistingPolicy.Disabled,
            };
            var staticDataViews = preset is FinalWasmOptimization.O2 or
                FinalWasmOptimization.O3 or FinalWasmOptimization.Oz
                ? StaticDataViewEliminationPolicy.Conservative
                : StaticDataViewEliminationPolicy.Disabled;
            var valuePromotion = preset is FinalWasmOptimization.O3 or FinalWasmOptimization.Oz
                ? AddressTakenValuePromotionPolicy.References
                : AddressTakenValuePromotionPolicy.Disabled;
            var methodInlining = preset switch
            {
                FinalWasmOptimization.O3 => ManagedMethodInliningPolicy.Speed,
                FinalWasmOptimization.O2 or FinalWasmOptimization.Oz =>
                    ManagedMethodInliningPolicy.Loops,
                FinalWasmOptimization.O1 => ManagedMethodInliningPolicy.Aggressive,
                _ => ManagedMethodInliningPolicy.Disabled,
            };
            var branchPrefixDuplication = preset switch
            {
                FinalWasmOptimization.O0 =>
                    BranchPrefixDuplicationPolicy.Disabled,
                FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or
                    FinalWasmOptimization.O3 =>
                    BranchPrefixDuplicationPolicy.Speed,
                _ => BranchPrefixDuplicationPolicy.SizeAware,
            };
            Assert.Equal(
                new(
                    preset,
                    selection,
                    runtimeSelection,
                    fieldHoisting,
                    staticDataViews,
                    valuePromotion,
                    methodInlining,
                    branchPrefixDuplication,
                    preset == FinalWasmOptimization.O3
                        ? ScalarCallPreservationPolicy.Conservative
                        : ScalarCallPreservationPolicy.Disabled,
                    preset is FinalWasmOptimization.O3 or FinalWasmOptimization.Oz
                        ? ArrayBoundsCheckEliminationPolicy.Conservative
                        : ArrayBoundsCheckEliminationPolicy.Disabled,
                    preset == FinalWasmOptimization.O3 ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
                    WholeProgramDefault(preset) ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
                    WholeProgramDefault(preset) ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
                    WholeProgramDefault(preset) ? ConstantCallFoldingPolicy.Conservative : ConstantCallFoldingPolicy.Disabled,
                    preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? SwitchBitTestPolicy.Disabled : SwitchBitTestPolicy.Conservative,
                    preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? DevirtualizationPolicy.Disabled : DevirtualizationPolicy.ExactType,
                    preset is FinalWasmOptimization.None or FinalWasmOptimization.O0 ? AllocationEliminationPolicy.Disabled : AllocationEliminationPolicy.Unused,
                    LoopRotation(preset),
                    preset is FinalWasmOptimization.None or FinalWasmOptimization.O0
                        ? ArrayStoreCheckEliminationPolicy.Disabled : ArrayStoreCheckEliminationPolicy.ExactElementTypes,
                    StaticInitializationHoisting(preset),
                    SmallBlockOperations(preset),
                    DispatcherElimination(preset),
                    EmptyFinallyRemoval(preset),
                    StructScalarReplacement(preset),
                    AllocationScalarReplacement(preset),
                    IdempotentLoopCollapse(preset),
                    LoopStoreForwarding(preset),
                    ArrayLoopIdioms(preset),
                    LoopVectorization(preset)),
                resolver.Resolve(preset, new(hint)));
        }
    }

    [Theory]
    [InlineData(ManagedLoopInvariantFieldHoisting.Disabled,
        LoopInvariantFieldHoistingPolicy.Disabled)]
    [InlineData(ManagedLoopInvariantFieldHoisting.Enabled,
        LoopInvariantFieldHoistingPolicy.Conservative)]
    [InlineData(ManagedLoopInvariantFieldHoisting.NullChecks,
        LoopInvariantFieldHoistingPolicy.NullChecks)]
    public void ExplicitFieldHoistingOverridesEveryEnabledPreset(
        ManagedLoopInvariantFieldHoisting fieldHoisting,
        LoopInvariantFieldHoistingPolicy expected)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                resolver.Resolve(
                    preset,
                    new(LoopInvariantFieldHoisting: fieldHoisting))
                    .LoopInvariantFieldHoisting);
        }
    }

    [Fact]
    public void DisabledFieldHoistingIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(LoopInvariantFieldHoisting:
                ManagedLoopInvariantFieldHoisting.Disabled));

        Assert.Equal(
            LoopInvariantFieldHoistingPolicy.Disabled,
            settings.LoopInvariantFieldHoisting);
    }

    [Fact]
    public void EnabledFieldHoistingRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(LoopInvariantFieldHoisting:
                ManagedLoopInvariantFieldHoisting.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedInliningHints.IgnoreAggressive)]
    [InlineData(ManagedInliningHints.Preserve)]
    public void RejectsIneffectiveOverridesWithNone(ManagedInliningHints hint)
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(FinalWasmOptimization.None, new(hint)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownPreset()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve((FinalWasmOptimization)255));
        Assert.Equal("preset", error.ParamName);
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    public void RejectsUnknownOverride(FinalWasmOptimization preset)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(preset, new((ManagedInliningHints)255)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownFieldHoistingOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(LoopInvariantFieldHoisting:
                (ManagedLoopInvariantFieldHoisting)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedStaticDataViewElimination.Disabled,
        StaticDataViewEliminationPolicy.Disabled)]
    [InlineData(ManagedStaticDataViewElimination.Enabled,
        StaticDataViewEliminationPolicy.Conservative)]
    public void ExplicitStaticDataViewPolicyOverridesEveryEnabledPreset(
        ManagedStaticDataViewElimination option,
        StaticDataViewEliminationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(StaticDataViewElimination: option))
                    .StaticDataViewElimination);
        }
    }

    [Fact]
    public void DisabledStaticDataViewEliminationIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(StaticDataViewElimination:
                ManagedStaticDataViewElimination.Disabled));

        Assert.Equal(
            StaticDataViewEliminationPolicy.Disabled,
            settings.StaticDataViewElimination);
    }

    [Fact]
    public void EnabledStaticDataViewEliminationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(StaticDataViewElimination:
                ManagedStaticDataViewElimination.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownStaticDataViewOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(StaticDataViewElimination:
                (ManagedStaticDataViewElimination)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedAddressTakenValuePromotion.Disabled,
        AddressTakenValuePromotionPolicy.Disabled)]
    [InlineData(ManagedAddressTakenValuePromotion.Enabled,
        AddressTakenValuePromotionPolicy.Conservative)]
    [InlineData(ManagedAddressTakenValuePromotion.References,
        AddressTakenValuePromotionPolicy.References)]
    public void ExplicitAddressTakenValuePromotionOverridesEveryEnabledPreset(
        ManagedAddressTakenValuePromotion option,
        AddressTakenValuePromotionPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(AddressTakenValuePromotion: option))
                    .AddressTakenValuePromotion);
        }
    }

    [Fact]
    public void DisabledAddressTakenValuePromotionIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(AddressTakenValuePromotion:
                ManagedAddressTakenValuePromotion.Disabled));

        Assert.Equal(
            AddressTakenValuePromotionPolicy.Disabled,
            settings.AddressTakenValuePromotion);
    }

    [Theory]
    [InlineData(ManagedAddressTakenValuePromotion.Enabled)]
    [InlineData(ManagedAddressTakenValuePromotion.References)]
    public void EnabledAddressTakenValuePromotionRequiresFinalOptimization(ManagedAddressTakenValuePromotion option)
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(AddressTakenValuePromotion: option)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownAddressTakenValuePromotionOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(AddressTakenValuePromotion:
                (ManagedAddressTakenValuePromotion)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedMethodInlining.Disabled, ManagedMethodInliningPolicy.Disabled)]
    [InlineData(ManagedMethodInlining.Enabled, ManagedMethodInliningPolicy.Speed)]
    [InlineData(ManagedMethodInlining.Loops, ManagedMethodInliningPolicy.Loops)]
    public void ExplicitManagedMethodInliningOverridesEveryEnabledPreset(
        ManagedMethodInlining option,
        ManagedMethodInliningPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(ManagedMethodInlining: option))
                    .ManagedMethodInlining);
        }
    }

    [Fact]
    public void DisabledManagedMethodInliningIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(ManagedMethodInlining: ManagedMethodInlining.Disabled));

        Assert.Equal(
            ManagedMethodInliningPolicy.Disabled,
            settings.ManagedMethodInlining);
    }

    [Theory]
    [InlineData(ManagedMethodInlining.Enabled)]
    [InlineData(ManagedMethodInlining.Loops)]
    public void EnabledManagedMethodInliningRequiresFinalOptimization(ManagedMethodInlining option)
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(ManagedMethodInlining: option)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownManagedMethodInliningOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(ManagedMethodInlining: (ManagedMethodInlining)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedBranchPrefixDuplication.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(ManagedBranchPrefixDuplication.Enabled,
        BranchPrefixDuplicationPolicy.Speed)]
    public void ExplicitBranchPrefixDuplicationOverridesEveryEnabledPreset(
        ManagedBranchPrefixDuplication option,
        BranchPrefixDuplicationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(BranchPrefixDuplication: option))
                    .BranchPrefixDuplication);
        }
    }

    [Fact]
    public void DisabledBranchPrefixDuplicationIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(BranchPrefixDuplication:
                ManagedBranchPrefixDuplication.Disabled));

        Assert.Equal(
            BranchPrefixDuplicationPolicy.Disabled,
            settings.BranchPrefixDuplication);
    }

    [Fact]
    public void EnabledBranchPrefixDuplicationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(BranchPrefixDuplication:
                ManagedBranchPrefixDuplication.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownBranchPrefixDuplicationOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(BranchPrefixDuplication:
                (ManagedBranchPrefixDuplication)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedScalarCallPreservation.Disabled, ScalarCallPreservationPolicy.Disabled)]
    [InlineData(ManagedScalarCallPreservation.Enabled, ScalarCallPreservationPolicy.Conservative)]
    public void ExplicitScalarCallPreservationOverridesEveryEnabledPreset(
        ManagedScalarCallPreservation option,
        ScalarCallPreservationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(expected, Create().Resolve(preset,
                new(ScalarCallPreservation: option)).ScalarCallPreservation);
        }
    }

    [Fact]
    public void DisabledScalarCallPreservationIsAcceptedWithoutFinalOptimization()
    {
        Assert.Equal(ScalarCallPreservationPolicy.Disabled,
            Create().Resolve(FinalWasmOptimization.None,
                new(ScalarCallPreservation: ManagedScalarCallPreservation.Disabled))
                .ScalarCallPreservation);
    }

    [Fact]
    public void EnabledScalarCallPreservationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(ScalarCallPreservation: ManagedScalarCallPreservation.Enabled)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownScalarCallPreservationOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(ScalarCallPreservation: (ManagedScalarCallPreservation)255)));
        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedArrayBoundsCheckElimination.Disabled, ArrayBoundsCheckEliminationPolicy.Disabled)]
    [InlineData(ManagedArrayBoundsCheckElimination.Enabled, ArrayBoundsCheckEliminationPolicy.Conservative)]
    public void ExplicitArrayBoundsSelectionOverridesEachEnabledPreset(
        ManagedArrayBoundsCheckElimination option, ArrayBoundsCheckEliminationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(expected, Create().Resolve(preset,
                new(ArrayBoundsCheckElimination: option)).ArrayBoundsCheckElimination);
        }
    }

    [Fact]
    public void DisabledArrayOptimizationsAreAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(FinalWasmOptimization.None,
            new(ArrayBoundsCheckElimination: ManagedArrayBoundsCheckElimination.Disabled,
                LoopUnrolling: ManagedLoopUnrolling.Disabled));

        Assert.Equal(ArrayBoundsCheckEliminationPolicy.Disabled, settings.ArrayBoundsCheckElimination);
        Assert.Equal(LoopUnrollingPolicy.Disabled, settings.LoopUnrolling);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnabledArrayOptimizationsRequireFinalOptimization(bool unrolling)
    {
        var options = unrolling
            ? new WasmOptimizationOverrides(LoopUnrolling: ManagedLoopUnrolling.Enabled)
            : new WasmOptimizationOverrides(ArrayBoundsCheckElimination: ManagedArrayBoundsCheckElimination.Enabled);

        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(FinalWasmOptimization.None, options));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUnknownArrayOptimizationSelections(bool unrolling)
    {
        var options = unrolling
            ? new WasmOptimizationOverrides(LoopUnrolling: (ManagedLoopUnrolling)255)
            : new WasmOptimizationOverrides(ArrayBoundsCheckElimination: (ManagedArrayBoundsCheckElimination)255);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(FinalWasmOptimization.O3, options));

        Assert.Equal("options", error.ParamName);
    }

    private static IWasmOptimizationSettingsResolver Create() =>
        Assert.IsAssignableFrom<IWasmOptimizationSettingsResolver>(new WasmOptimizationSettingsResolver());
}
