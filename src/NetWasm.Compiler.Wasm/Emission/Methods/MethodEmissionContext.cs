// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Emission.Arrays;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.StructResults;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record MethodEmissionContext(
    MethodRootMap RootMap,
    int LocalBase,
    int StackBase,
    EvaluationStackLocalLayout StackLocals,
    int ObjectTemporary,
    int RootFrame,
    int ExceptionTemporary,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionFrameLocals,
    IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionContinuationLocals,
    int ValueFrame,
    ValueFrameLayout ValueLayout,
    int FilterRootFrame,
    FilterEnvironmentLayout FilterEnvironment,
    int ParameterOffset,
    int InteropDescriptor,
    int InteropResult,
    int InteropHandle,
    int NumericTemporaryI4,
    int NumericTemporaryI8,
    int DispatcherProgramCounter,
    StructuredExceptionGroup? ActiveExceptionGroup = null,
    bool IsFilterFunclet = false,
    int StackTraceMethodId = 0,
    RuntimeImportSelection RuntimeImportSelection = default,
    ManagedMethodIdentity CallerIdentity = default)
{
    // Shared exception definitions can have several emitted occurrences. Copies
    // execute normally but must not each claim ownership of definition blocks.
    public HashSet<StructuredExceptionGroupId> EmittedExceptionGroups { get; init; } = [];

    public bool RecordOriginalBlockOwnership { get; init; } = true;

    // Which loops test their condition before entry and at the bottom of each iteration.
    public LoopRotationPolicy LoopRotation { get; init; }

    public ArrayMetadataCachingLayout ArrayMetadata { get; init; } = ArrayMetadataCachingLayout.Empty;


    public ArrayLengthRunLayout ArrayLengthRuns { get; init; } = ArrayLengthRunLayout.Empty;

    // Reference stores whose array has an exact element type need no covariant store check.
    public ImmutableHashSet<int> ExactArrayStores { get; init; } = [];

    public TerminalBranchValueFramePlacement? TerminalBranchValueFramePlacement { get; init; }

    // The protected blocks without catch clauses that keep their start's frame-arena marks in locals rather than in an
    // exception frame; any other block registers a frame.
    public ImmutableDictionary<StructuredExceptionGroupId, RegionMarkLocals> RegionMarkLocals { get; init; } =
        ImmutableDictionary<StructuredExceptionGroupId, RegionMarkLocals>.Empty;

    // The marks read after the prologue, for a method with protected blocks (RegionMarkLocals).
    public RegionMarkLocals? EntryMarks { get; init; }

    // Where each protected block's landing pad returns the frame arenas to (ILandingPadRestorePlanner).
    public ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore> LandingPadRestores { get; init; } =
        ImmutableDictionary<StructuredExceptionGroupId, LandingPadRestore>.Empty;

    // The locals fields returned in Wasm results move through, at the method's own return and after its calls.
    public StructResultLocals StructResultLocals { get; init; } = StructResultLocals.Empty;

    // Each argument's first Wasm local; a struct argument passed by its fields takes one per field.
    public ImmutableArray<int> ArgumentLocals { get; init; } = [];

    // The struct arguments passed by their fields, with their shapes and first parameters.
    public ImmutableDictionary<int, FieldArgument> FieldArguments { get; init; } =
        ImmutableDictionary<int, FieldArgument>.Empty;

    // Field arguments read only through their fields: the address pushes that push nothing, and each field
    // load's parameter.
    public ImmutableHashSet<int> FieldArgumentAddresses { get; init; } = [];

    public ImmutableDictionary<int, (int Parameter, CliValueKind Kind)> FieldArgumentLoads { get; init; } =
        ImmutableDictionary<int, (int, CliValueKind)>.Empty;

    // Field arguments read in place, and the calls (by call offset and argument position) that take one of
    // them whole, which push its parameters, with the whole loads that push nothing.
    public ImmutableDictionary<int, FieldArgument> InPlaceFieldArguments { get; init; } =
        ImmutableDictionary<int, FieldArgument>.Empty;

    public ImmutableDictionary<(int Call, int Argument), int> ForwardedFieldArguments { get; init; } =
        ImmutableDictionary<(int, int), int>.Empty;

    public ImmutableHashSet<int> ForwardedArgumentLoads { get; init; } = [];

    // The returns, by offset, of an argument in place, which return its parameters.
    public ImmutableDictionary<int, int> ReturnedFieldArguments { get; init; } = ImmutableDictionary<int, int>.Empty;

    // Struct temporaries passed to calls from lanes of their own, and those lanes by temporary.
    public ArgumentLanePlan ArgumentLanes { get; init; } = ArgumentLanePlan.Empty;

    public ImmutableDictionary<int, ImmutableArray<int>> ArgumentLaneLocals { get; init; } =
        ImmutableDictionary<int, ImmutableArray<int>>.Empty;

    // The factory's flat list of those lanes.
    public ImmutableArray<int> ArgumentLaneFlat { get; init; } = [];

    // The address locals the factory allocated for them, in argument order.
    public ImmutableArray<int> FieldArgumentAddressLocals { get; init; } = [];

    // The Wasm local holding the argument (a struct argument passed by fields: its first field).
    public int ArgumentLocal(int argument) => ArgumentLocals.IsDefaultOrEmpty ? ParameterOffset + argument : ArgumentLocals[argument];

    // The returned local kept in the return shape's lanes, if any.
    public ReturnLanePlan? ReturnLanes { get; init; }

    // Calls whose value-type results stay in lanes.
    public CallResultLanePlan CallResultLanes { get; init; } = CallResultLanePlan.Empty;

    public TransientAggregatePromotionLayout TransientAggregatePromotions { get; init; } =
        TransientAggregatePromotionLayout.Empty;

    public AddressTakenScalarPromotionLayout AddressTakenScalarPromotions { get; init; } =
        AddressTakenScalarPromotionLayout.Empty;

    public AddressTakenAggregatePromotionLayout AddressTakenAggregatePromotions
    { get; init; } = AddressTakenAggregatePromotionLayout.Empty;

    public ManagedByRefAggregatePromotionLayout ManagedByRefAggregatePromotions
    { get; init; } = ManagedByRefAggregatePromotionLayout.Empty;

    public ManagedAggregateValueLayout ManagedAggregateValues { get; init; } =
        ManagedAggregateValueLayout.Empty;

    // The module's shared function that begins and throws a managed exception.
    public OptionalFunctionIndex ManagedThrowFunction { get; init; }

    // Address scratch for known-size copies emitted inline; -1 where the
    // function has none, and its copies stay memory.copy.
    public int CopyDestinationTemporary { get; init; } = -1;
    public bool InlineSmallBlocks { get; init; }
    // One-dimensional array elements are addressed at the inline offset every
    // runtime keeps them at, without reading the array's data pointer.
    public bool InlineArrayData { get; init; }
    public int CopySourceTemporary { get; init; } = -1;
    // Whether newobj may publish its roots only when its inline allocation
    // calls the collector, so later root elisions cannot rely on it.
    public bool ConditionalAllocationRoots { get; init; }

    // Whether the program reaches a protected block with a filter (WasmEmissionRequest.ReachesFilters).
    public bool ReachesFilters { get; init; } = true;

    public ImmutableDictionary<int, int> AnchorLocals { get; init; } = [];
    public ImmutableDictionary<RootSource, int> AuthoritativeRootSlots { get; init; } = [];
    public ValueFrameReferenceMap ValueFrameReferences { get; init; } = ValueFrameReferenceMap.Empty;
    public ImmutableDictionary<(int Offset, bool ConstructorCall), ImmutableHashSet<int>> RootClearElisions
    { get; init; } = [];
    public ImmutableDictionary<int, ImmutableHashSet<int>> RootValueElisions { get; init; } = [];

    // Whether the method zeroes its root frame on entry, so every slot holds null until written.
    public bool RootFrameStartsZeroed { get; init; }

    // Each protected block's local for the one-slot root frame its landing pad enters to hold the exception in flight
    // (0 until then): a frame of its own, so a method whose only references are exceptions enters no frame on its
    // normal path.
    public IReadOnlyDictionary<StructuredExceptionGroupId, int> ExceptionCellFrames { get; init; } =
        ImmutableDictionary<StructuredExceptionGroupId, int>.Empty;
    // Set when the method enters its root frame at its first publication rather than on entry.
    public LazyRootFramePlan? LazyRootFrame { get; init; }

    // Each protected block's copy of the root frame local as it begins, which its landing pad restores (lazy frames).
    public IReadOnlyDictionary<StructuredExceptionGroupId, int> RootFrameSaves { get; init; } =
        ImmutableDictionary<StructuredExceptionGroupId, int>.Empty;

    // The exception cell frame of the catch whose handler is being emitted, which a rethrow reads, or null.
    public int? ActiveCatchCellFrame { get; init; }
    // The frame local of the pass-through catch whose handler is being emitted (0 while it is passed by), or null.
    public int? ActiveCatchPassThroughToken { get; init; }
    public int RootSlotCount => RootMap.SlotCount;

    // WasmLocalLayoutPlanner.TemporaryLocals declares these after the dispatcher program counter.
    public int NumericTemporaryI4Second => checked(DispatcherProgramCounter + 1);

    // The exception-frame token of a catch the compiler adds inside a method body.
    public int InlineCatchFrame => checked(DispatcherProgramCounter + 2);

    // The address and value a 12-byte vector load or store moves through.
    public VectorScratch VectorScratch =>
        new(checked(DispatcherProgramCounter + 3), checked(DispatcherProgramCounter + 4));
}

// A struct argument passed by its fields: its shape and the Wasm parameter of its first field.
internal sealed record FieldArgument(StructResults.StructResultShape Shape, int FirstParameter);
