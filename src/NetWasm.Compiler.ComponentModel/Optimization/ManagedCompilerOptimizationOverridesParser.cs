using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed class ManagedCompilerOptimizationOverridesParser :
    IManagedCompilerOptimizationOverridesParser
{
    private static readonly FrozenDictionary<string, ManagedScalarCallPreservation>
        ScalarCallPreservationValues =
        new Dictionary<string, ManagedScalarCallPreservation>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedScalarCallPreservation.Auto,
            ["Disabled"] = ManagedScalarCallPreservation.Disabled,
            ["Enabled"] = ManagedScalarCallPreservation.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedLoopInvariantFieldHoisting>
        FieldHoistingValues =
        new Dictionary<string, ManagedLoopInvariantFieldHoisting>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedLoopInvariantFieldHoisting.Auto,
            ["Disabled"] = ManagedLoopInvariantFieldHoisting.Disabled,
            ["Enabled"] = ManagedLoopInvariantFieldHoisting.Enabled,
            ["NullChecks"] = ManagedLoopInvariantFieldHoisting.NullChecks,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedStaticDataViewElimination>
        StaticDataViewValues =
        new Dictionary<string, ManagedStaticDataViewElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedStaticDataViewElimination.Auto,
            ["Disabled"] = ManagedStaticDataViewElimination.Disabled,
            ["Enabled"] = ManagedStaticDataViewElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedAddressTakenValuePromotion>
        AddressTakenValuePromotionValues =
        new Dictionary<string, ManagedAddressTakenValuePromotion>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedAddressTakenValuePromotion.Auto,
            ["Disabled"] = ManagedAddressTakenValuePromotion.Disabled,
            ["Enabled"] = ManagedAddressTakenValuePromotion.Enabled,
            ["References"] = ManagedAddressTakenValuePromotion.References,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedMethodInlining>
        ManagedMethodInliningValues =
        new Dictionary<string, ManagedMethodInlining>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedMethodInlining.Auto,
            ["Disabled"] = ManagedMethodInlining.Disabled,
            ["Enabled"] = ManagedMethodInlining.Enabled,
            ["Loops"] = ManagedMethodInlining.Loops,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedBranchPrefixDuplication>
        BranchPrefixDuplicationValues =
        new Dictionary<string, ManagedBranchPrefixDuplication>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedBranchPrefixDuplication.Auto,
            ["Disabled"] = ManagedBranchPrefixDuplication.Disabled,
            ["Enabled"] = ManagedBranchPrefixDuplication.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedArrayBoundsCheckElimination> ArrayBoundsCheckEliminationValues =
        new Dictionary<string, ManagedArrayBoundsCheckElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedArrayBoundsCheckElimination.Auto,
            ["Disabled"] = ManagedArrayBoundsCheckElimination.Disabled,
            ["Enabled"] = ManagedArrayBoundsCheckElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedLoopUnrolling> LoopUnrollingValues =
        new Dictionary<string, ManagedLoopUnrolling>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedLoopUnrolling.Auto,
            ["Disabled"] = ManagedLoopUnrolling.Disabled,
            ["Enabled"] = ManagedLoopUnrolling.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedConstantArgumentPropagation> ConstantArgumentPropagationValues =
        new Dictionary<string, ManagedConstantArgumentPropagation>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedConstantArgumentPropagation.Auto,
            ["Disabled"] = ManagedConstantArgumentPropagation.Disabled,
            ["Enabled"] = ManagedConstantArgumentPropagation.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedStaticArrayPopulationElimination> StaticArrayPopulationEliminationValues =
        new Dictionary<string, ManagedStaticArrayPopulationElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedStaticArrayPopulationElimination.Auto,
            ["Disabled"] = ManagedStaticArrayPopulationElimination.Disabled,
            ["Enabled"] = ManagedStaticArrayPopulationElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedConstantCallFolding> ConstantCallFoldingValues =
        new Dictionary<string, ManagedConstantCallFolding>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedConstantCallFolding.Auto,
            ["Disabled"] = ManagedConstantCallFolding.Disabled,
            ["Enabled"] = ManagedConstantCallFolding.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedSwitchBitTests> SwitchBitTestValues =
        new Dictionary<string, ManagedSwitchBitTests>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedSwitchBitTests.Auto,
            ["Disabled"] = ManagedSwitchBitTests.Disabled,
            ["Enabled"] = ManagedSwitchBitTests.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedDevirtualization> DevirtualizationValues =
        new Dictionary<string, ManagedDevirtualization>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedDevirtualization.Auto,
            ["Disabled"] = ManagedDevirtualization.Disabled,
            ["Enabled"] = ManagedDevirtualization.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedAllocationElimination> AllocationEliminationValues =
        new Dictionary<string, ManagedAllocationElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedAllocationElimination.Auto,
            ["Disabled"] = ManagedAllocationElimination.Disabled,
            ["Enabled"] = ManagedAllocationElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedLoopRotation> LoopRotationValues =
        new Dictionary<string, ManagedLoopRotation>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedLoopRotation.Auto,
            ["Disabled"] = ManagedLoopRotation.Disabled,
            ["Enabled"] = ManagedLoopRotation.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedArrayStoreCheckElimination>
        ArrayStoreCheckEliminationValues =
        new Dictionary<string, ManagedArrayStoreCheckElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedArrayStoreCheckElimination.Auto,
            ["Disabled"] = ManagedArrayStoreCheckElimination.Disabled,
            ["Enabled"] = ManagedArrayStoreCheckElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedDispatcherElimination>
        DispatcherEliminationValues =
        new Dictionary<string, ManagedDispatcherElimination>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedDispatcherElimination.Auto,
            ["Disabled"] = ManagedDispatcherElimination.Disabled,
            ["Enabled"] = ManagedDispatcherElimination.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedEmptyFinallyRemoval>
        EmptyFinallyRemovalValues =
        new Dictionary<string, ManagedEmptyFinallyRemoval>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedEmptyFinallyRemoval.Auto,
            ["Disabled"] = ManagedEmptyFinallyRemoval.Disabled,
            ["Enabled"] = ManagedEmptyFinallyRemoval.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedArrayLoopIdioms>
        ArrayLoopIdiomValues =
        new Dictionary<string, ManagedArrayLoopIdioms>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedArrayLoopIdioms.Auto,
            ["Disabled"] = ManagedArrayLoopIdioms.Disabled,
            ["Enabled"] = ManagedArrayLoopIdioms.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedLoopVectorization>
        LoopVectorizationValues =
        new Dictionary<string, ManagedLoopVectorization>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedLoopVectorization.Auto,
            ["Disabled"] = ManagedLoopVectorization.Disabled,
            ["Enabled"] = ManagedLoopVectorization.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedLoopStoreForwarding>
        LoopStoreForwardingValues =
        new Dictionary<string, ManagedLoopStoreForwarding>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedLoopStoreForwarding.Auto,
            ["Disabled"] = ManagedLoopStoreForwarding.Disabled,
            ["Enabled"] = ManagedLoopStoreForwarding.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedIdempotentLoopCollapse>
        IdempotentLoopCollapseValues =
        new Dictionary<string, ManagedIdempotentLoopCollapse>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedIdempotentLoopCollapse.Auto,
            ["Disabled"] = ManagedIdempotentLoopCollapse.Disabled,
            ["Enabled"] = ManagedIdempotentLoopCollapse.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedAllocationScalarReplacement>
        AllocationScalarReplacementValues =
        new Dictionary<string, ManagedAllocationScalarReplacement>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedAllocationScalarReplacement.Auto,
            ["Disabled"] = ManagedAllocationScalarReplacement.Disabled,
            ["Enabled"] = ManagedAllocationScalarReplacement.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ManagedStructScalarReplacement>
        StructScalarReplacementValues =
        new Dictionary<string, ManagedStructScalarReplacement>(StringComparer.Ordinal)
        {
            ["Auto"] = ManagedStructScalarReplacement.Auto,
            ["Disabled"] = ManagedStructScalarReplacement.Disabled,
            ["Enabled"] = ManagedStructScalarReplacement.Enabled,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public WasmOptimizationOverrides? Parse(
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
        string? loopVectorization = null)
    {
        var fieldHoisting = ParseFieldHoisting(loopInvariantFieldHoisting);
        var staticDataViews = ParseStaticDataViews(staticDataViewElimination);
        var valuePromotion = ParseAddressTakenValuePromotion(
            addressTakenValuePromotion);
        var methodInlining = ParseManagedMethodInlining(managedMethodInlining);
        var prefixDuplication = ParseBranchPrefixDuplication(
            branchPrefixDuplication);
        var scalarCalls = ScalarCallPreservationValues.TryGetValue(scalarCallPreservation ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid("Managed scalar-call preservation must be Auto, Disabled or Enabled.");
        var arrayBoundsCheckEliminationValue = ArrayBoundsCheckEliminationValues.TryGetValue(arrayBoundsCheckElimination ?? "Auto", out var arrayBoundsCheckEliminationParsed)
            ? arrayBoundsCheckEliminationParsed
            : throw ComponentException.Invalid("Managed array bounds check elimination must be Auto, Disabled or Enabled.");
        var loopUnrollingValue = LoopUnrollingValues.TryGetValue(loopUnrolling ?? "Auto", out var loopUnrollingParsed)
            ? loopUnrollingParsed
            : throw ComponentException.Invalid("Managed loop unrolling must be Auto, Disabled or Enabled.");
        var constantArgumentPropagationValue = ConstantArgumentPropagationValues.TryGetValue(constantArgumentPropagation ?? "Auto", out var constantArgumentPropagationParsed)
            ? constantArgumentPropagationParsed
            : throw ComponentException.Invalid("Managed constant argument propagation must be Auto, Disabled or Enabled.");
        var staticArrayPopulationEliminationValue = StaticArrayPopulationEliminationValues.TryGetValue(staticArrayPopulationElimination ?? "Auto", out var staticArrayPopulationEliminationParsed)
            ? staticArrayPopulationEliminationParsed
            : throw ComponentException.Invalid("Managed static array population elimination must be Auto, Disabled or Enabled.");
        var constantCallFoldingValue = ConstantCallFoldingValues.TryGetValue(constantCallFolding ?? "Auto", out var constantCallFoldingParsed)
            ? constantCallFoldingParsed
            : throw ComponentException.Invalid("Managed constant call folding must be Auto, Disabled or Enabled.");
        var switchBitTestsValue = SwitchBitTestValues.TryGetValue(switchBitTests ?? "Auto", out var switchBitTestsParsed)
            ? switchBitTestsParsed
            : throw ComponentException.Invalid("Managed switch bit tests must be Auto, Disabled or Enabled.");
        var devirtualizationValue = DevirtualizationValues.TryGetValue(devirtualization ?? "Auto", out var devirtualizationParsed)
            ? devirtualizationParsed
            : throw ComponentException.Invalid("Managed devirtualization must be Auto, Disabled or Enabled.");
        var allocationEliminationValue = AllocationEliminationValues.TryGetValue(allocationElimination ?? "Auto", out var allocationEliminationParsed)
            ? allocationEliminationParsed
            : throw ComponentException.Invalid("Managed allocation elimination must be Auto, Disabled or Enabled.");
        var loopRotationValue = LoopRotationValues.TryGetValue(loopRotation ?? "Auto", out var loopRotationParsed)
            ? loopRotationParsed
            : throw ComponentException.Invalid("Managed loop rotation must be Auto, Disabled or Enabled.");
        var arrayStoreChecksValue = ArrayStoreCheckEliminationValues.TryGetValue(
            arrayStoreCheckElimination ?? "Auto", out var arrayStoreChecksParsed)
            ? arrayStoreChecksParsed
            : throw ComponentException.Invalid(
                "Managed array store check elimination must be Auto, Disabled or Enabled.");
        var dispatchersValue = DispatcherEliminationValues.TryGetValue(
            dispatcherElimination ?? "Auto", out var dispatchersParsed)
            ? dispatchersParsed
            : throw ComponentException.Invalid(
                "Managed dispatcher elimination must be Auto, Disabled or Enabled.");
        var emptyFinallysValue = EmptyFinallyRemovalValues.TryGetValue(
            emptyFinallyRemoval ?? "Auto", out var emptyFinallysParsed)
            ? emptyFinallysParsed
            : throw ComponentException.Invalid(
                "Managed empty finally removal must be Auto, Disabled or Enabled.");
        var structsValue = StructScalarReplacementValues.TryGetValue(
            structScalarReplacement ?? "Auto", out var structsParsed)
            ? structsParsed
            : throw ComponentException.Invalid(
                "Managed struct scalar replacement must be Auto, Disabled or Enabled.");
        var allocationsValue = AllocationScalarReplacementValues.TryGetValue(
            allocationScalarReplacement ?? "Auto", out var allocationsParsed)
            ? allocationsParsed
            : throw ComponentException.Invalid(
                "Managed allocation scalar replacement must be Auto, Disabled or Enabled.");
        var idempotentLoopsValue = IdempotentLoopCollapseValues.TryGetValue(
            idempotentLoopCollapse ?? "Auto", out var idempotentLoopsParsed)
            ? idempotentLoopsParsed
            : throw ComponentException.Invalid(
                "Managed idempotent loop collapse must be Auto, Disabled or Enabled.");
        var loopStoresValue = LoopStoreForwardingValues.TryGetValue(
            loopStoreForwarding ?? "Auto", out var loopStoresParsed)
            ? loopStoresParsed
            : throw ComponentException.Invalid(
                "Managed loop store forwarding must be Auto, Disabled or Enabled.");
        var arrayLoopsValue = ArrayLoopIdiomValues.TryGetValue(
            arrayLoopIdioms ?? "Auto", out var arrayLoopsParsed)
            ? arrayLoopsParsed
            : throw ComponentException.Invalid(
                "Managed array loop idioms must be Auto, Disabled or Enabled.");
        var vectorizationValue = LoopVectorizationValues.TryGetValue(
            loopVectorization ?? "Auto", out var vectorizationParsed)
            ? vectorizationParsed
            : throw ComponentException.Invalid(
                "Managed loop vectorization must be Auto, Disabled or Enabled.");
        return fieldHoisting == ManagedLoopInvariantFieldHoisting.Auto &&
               staticDataViews == ManagedStaticDataViewElimination.Auto &&
               valuePromotion == ManagedAddressTakenValuePromotion.Auto &&
               methodInlining == ManagedMethodInlining.Auto &&
               prefixDuplication == ManagedBranchPrefixDuplication.Auto &&
               scalarCalls == ManagedScalarCallPreservation.Auto &&
               arrayBoundsCheckEliminationValue == ManagedArrayBoundsCheckElimination.Auto &&
               loopUnrollingValue == ManagedLoopUnrolling.Auto &&
               constantArgumentPropagationValue == ManagedConstantArgumentPropagation.Auto &&
               staticArrayPopulationEliminationValue == ManagedStaticArrayPopulationElimination.Auto &&
               constantCallFoldingValue == ManagedConstantCallFolding.Auto &&
               switchBitTestsValue == ManagedSwitchBitTests.Auto &&
               devirtualizationValue == ManagedDevirtualization.Auto &&
               allocationEliminationValue == ManagedAllocationElimination.Auto &&
               loopRotationValue == ManagedLoopRotation.Auto &&
               arrayStoreChecksValue == ManagedArrayStoreCheckElimination.Auto &&
               dispatchersValue == ManagedDispatcherElimination.Auto &&
               emptyFinallysValue == ManagedEmptyFinallyRemoval.Auto &&
               structsValue == ManagedStructScalarReplacement.Auto &&
               allocationsValue == ManagedAllocationScalarReplacement.Auto &&
               idempotentLoopsValue == ManagedIdempotentLoopCollapse.Auto &&
               loopStoresValue == ManagedLoopStoreForwarding.Auto &&
               arrayLoopsValue == ManagedArrayLoopIdioms.Auto &&
               vectorizationValue == ManagedLoopVectorization.Auto
            ? null
            : new(
                LoopInvariantFieldHoisting: fieldHoisting,
                StaticDataViewElimination: staticDataViews,
                AddressTakenValuePromotion: valuePromotion,
                ManagedMethodInlining: methodInlining,
                BranchPrefixDuplication: prefixDuplication,
                ScalarCallPreservation: scalarCalls,
                ArrayBoundsCheckElimination: arrayBoundsCheckEliminationValue,
                LoopUnrolling: loopUnrollingValue,
                ConstantArgumentPropagation: constantArgumentPropagationValue,
                StaticArrayPopulationElimination: staticArrayPopulationEliminationValue,
                ConstantCallFolding: constantCallFoldingValue,
                SwitchBitTests: switchBitTestsValue,
                Devirtualization: devirtualizationValue,
                AllocationElimination: allocationEliminationValue,
                LoopRotation: loopRotationValue,
                ArrayStoreCheckElimination: arrayStoreChecksValue,
                DispatcherElimination: dispatchersValue,
                EmptyFinallyRemoval: emptyFinallysValue,
                StructScalarReplacement: structsValue,
                AllocationScalarReplacement: allocationsValue,
                IdempotentLoopCollapse: idempotentLoopsValue,
                LoopStoreForwarding: loopStoresValue,
                ArrayLoopIdioms: arrayLoopsValue,
                LoopVectorization: vectorizationValue);
    }

    private static ManagedLoopInvariantFieldHoisting ParseFieldHoisting(string? value) =>
        FieldHoistingValues.TryGetValue(value ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid(
                "Managed loop-invariant field hoisting must be Auto, Disabled or Enabled.");

    private static ManagedStaticDataViewElimination ParseStaticDataViews(string? value) =>
        StaticDataViewValues.TryGetValue(value ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid(
                "Managed static-data-view elimination must be Auto, Disabled or Enabled.");

    private static ManagedAddressTakenValuePromotion ParseAddressTakenValuePromotion(
        string? value) =>
        AddressTakenValuePromotionValues.TryGetValue(value ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid(
                "Managed address-taken value promotion must be Auto, Disabled or Enabled.");

    private static ManagedMethodInlining ParseManagedMethodInlining(string? value) =>
        ManagedMethodInliningValues.TryGetValue(value ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid(
                "Managed method inlining must be Auto, Disabled, Enabled or Loops.");

    private static ManagedBranchPrefixDuplication ParseBranchPrefixDuplication(
        string? value) =>
        BranchPrefixDuplicationValues.TryGetValue(value ?? "Auto", out var parsed)
            ? parsed
            : throw ComponentException.Invalid(
                "Managed branch-prefix duplication must be Auto, Disabled or Enabled.");
}
