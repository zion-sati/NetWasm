using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class RootPublicationEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, true, false)]
    [InlineData(WasmTarget.Wasm64, true, false)]
    [InlineData(WasmTarget.Wasm32, false, false)]
    [InlineData(WasmTarget.Wasm64, false, false)]
    [InlineData(WasmTarget.Wasm32, true, true)]
    [InlineData(WasmTarget.Wasm64, true, true)]
    public void UnchangedValueProofNeverSkipsDeadClearsOrConstructorPublication(
        WasmTarget target, bool live, bool constructor)
    {
        var source = new RootSource(RootSourceKind.Argument, 0);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0,
                    new(0, live ? [source] : [], constructor ? [source] : []))),
            RootValueElisions = ImmutableDictionary<int, ImmutableHashSet<int>>.Empty.Add(0, [0]),
        };
        var code = new RecordingInstructionWriter();
        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))
            .Emit(CreateInstructionRequest(CilOperation.Call, context: context), code, constructor);
        if (live && !constructor) Assert.Empty(code.ToInstructions());
        else Assert.Single(code.ToInstructions(), instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void KnownZeroProofSkipsOnlyInactiveSlots(WasmTarget target, bool live)
    {
        var source = new RootSource(RootSourceKind.Local, 0);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, live ? [source] : [], []))),
            RootClearElisions = ImmutableDictionary<(int, bool), ImmutableHashSet<int>>.Empty.Add((0, false), [0]),
        };
        var code = new RecordingInstructionWriter();
        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))
            .Emit(CreateInstructionRequest(CilOperation.Call, context: context), code);
        if (!live) Assert.Empty(code.ToInstructions());
        else Assert.Single(code.ToInstructions(), instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    public void AuthoritativeLiveReferencesNeedNoCopyButDeadOnesAreCleared(WasmTarget target, bool live)
    {
        var source = new RootSource(RootSourceKind.Local, 0);
        var slots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 0);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey, slots, ImmutableDictionary<int, SafepointRootMap>.Empty
                .Add(0, new(0, live ? [source] : [], []))),
            AuthoritativeRootSlots = slots,
        };
        var code = new RecordingInstructionWriter();
        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))
            .Emit(CreateInstructionRequest(CilOperation.Call, context: context), code);
        if (live) Assert.Empty(code.ToInstructions());
        else
        {
            var instructions = code.ToInstructions();
            Assert.Single(instructions.Where(i => i.Opcode ==
                (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store)));
            Assert.Contains(instructions, i => i.Opcode ==
                (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Constant : WasmOpcodes.I64Constant));
        }
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InactiveBackingVariantsAreGuardedBeforeAnyIndirectFieldRead(WasmTarget target)
    {
        var pointerSize = WasmTargetLayout.For(target).AddressSize;
        var source = new RootSource(RootSourceKind.StackStorageAnchor, 0, pointerSize);
        var context = CreateMethodEmissionContext() with
        {
            AnchorLocals = ImmutableDictionary<int, int>.Empty.Add(0, 100),
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, [source], [])))
            {
                AnchorPlan = new([new(new(StackStorageLocationKind.EvaluationStack, 0),
                    CliTypeIdentity.Named(Assembly, "Tests", "Pair", true))], []),
            },
        };
        var request = CreateInstructionRequest(CilOperation.Call, context: context);
        var code = new RecordingInstructionWriter();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        CreateEmitter(layouts).Emit(request, code);
        var instructions = code.ToInstructions();
        var branch = instructions.IndexOf(Assert.Single(instructions.Where(instruction => instruction.Opcode == WasmOpcodes.If)));
        var otherwise = instructions.IndexOf(Assert.Single(instructions.Where(instruction => instruction.Opcode == WasmOpcodes.Else)));
        var load = Assert.Single(instructions.Where(instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load)));
        Assert.True(branch < otherwise && otherwise < instructions.IndexOf(load));
        Assert.Equal((uint)pointerSize, load.Operand.Offset);
        Assert.Contains(instructions, instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero));
        Assert.Equal(2, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand == WasmInstructionOperand.Unsigned(100)));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ValueType)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ValueType)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedAddress)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedAddress)]
    public void CapturedVariablesPublishCurrentEnvironmentStorageInsteadOfStaleParentLocals(WasmTarget target, CliValueKind kind)
    {
        var pointerSize = WasmTargetLayout.For(target).AddressSize;
        var pair = CliTypeIdentity.Named(Assembly, "Tests", "Pair", true);
        var type = kind == CliValueKind.ManagedAddress ? CliTypeIdentity.ManagedByReference(pair) :
            kind == CliValueKind.ValueType ? pair : CliTypeIdentity.FromStackKind(kind);
        var slot = new CapturedSlot(false, 0);
        var source = new RootSource(kind == CliValueKind.ManagedAddress ? RootSourceKind.ManagedAddressLocal : RootSourceKind.Local,
            0, kind == CliValueKind.ManagedReference ? -1 : pointerSize);
        var context = CreateMethodEmissionContext() with
        {
            LocalBase = 8,
            ValueFrame = 90,
            RootMap = new(EntryKey, ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, [source], []))),
            FilterEnvironment = new(32, 0, 0, ImmutableDictionary<CapturedSlot, FilterCapture>.Empty.Add(slot,
                new(slot, type, 16, []))),
        };
        var code = new RecordingInstructionWriter();
        var request = CreateInstructionRequest(CilOperation.Call, context: context);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        CreateEmitter(layouts).Emit(request, code);
        var instructions = code.ToInstructions();
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand == WasmInstructionOperand.Unsigned(8));
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand == WasmInstructionOperand.Unsigned(90));
        var loads = instructions.Where(instruction => instruction.Opcode ==
            (target == WasmTarget.Wasm64 ? WasmOpcodes.I64Load : WasmOpcodes.I32Load)).ToArray();
        Assert.Equal(kind == CliValueKind.ManagedAddress ? 3 : 1, loads.Length);
        Assert.Equal(kind == CliValueKind.ManagedReference ? 0u : (uint)pointerSize, loads[^1].Operand.Offset);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SafepointPublicationDoesNotClearPendingExceptionRoots(WasmTarget target)
    {
        var source = new RootSource(RootSourceKind.Argument, 0);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new MethodRootMap(EntryKey,
                ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, [source], []))),
            ExceptionRootSlots = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(new(0), 1),
            IsFilterFunclet = true,
        };
        var request = CreateInstructionRequest(CilOperation.Call, context: context);
        var code = new RecordingInstructionWriter();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = CreateEmitter(layouts);
        emitter.Emit(request, code);
        var instructions = code.ToInstructions();
        Assert.Single(instructions.Where(
            instruction => instruction.Opcode is WasmOpcodes.I32Store or WasmOpcodes.I64Store));
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode is WasmOpcodes.I32Add or WasmOpcodes.I64Add);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Store)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Store)]
    public void WritesEachActiveOrInactiveRootSlotExactlyOnce(
        WasmTarget target,
        byte expectedStore)
    {
        var active = new RootSource(RootSourceKind.Argument, 0);
        var inactive = new RootSource(RootSourceKind.Local, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(active, 0)
                .Add(inactive, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [active], [])));
        var context = CreateMethodEmissionContext() with { RootMap = rootMap };
        var request = CreateInstructionRequest(CilOperation.Call, context: context);
        var code = new RecordingInstructionWriter();

        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))
            .Emit(request, code);

        var instructions = code.ToInstructions();
        Assert.Equal(2, instructions.Count(instruction => instruction.Opcode == expectedStore));
        Assert.Equal(3, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.LocalGet));
        var expectedZero = target == WasmTarget.Wasm64
            ? WasmInstructionOperand.Signed64(0)
            : WasmInstructionOperand.Signed(0);
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == (target == WasmTarget.Wasm64
                ? WasmOpcodes.I64Constant
                : WasmOpcodes.I32Constant) &&
            instruction.Operand == expectedZero);
    }

    [Fact]
    public void PublishesNormalAndConstructorRootSets()
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(argument, 0)
                .Add(allocation, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [argument], [argument, allocation])));
        var context = CreateMethodEmissionContext() with { RootMap = rootMap };
        var normal = CreateInstructionRequest(
            CilOperation.Call,
            context: context);
        var constructor = CreateInstructionRequest(
            CilOperation.NewObject,
            context: context);
        var emitter = CreateEmitter();
        var normalCode = new RecordingInstructionWriter();
        var constructorCode = new RecordingInstructionWriter();

        EmitThroughCapability(emitter, normal, normalCode);
        EmitThroughCapability(emitter, constructor, constructorCode, constructorCall: true);

        var normalInstructions = normalCode.ToInstructions();
        var constructorInstructions = constructorCode.ToInstructions();
        Assert.Equal(2, normalInstructions.Count(
            instruction => instruction.Opcode == WasmOpcodes.I32Store));
        Assert.Equal(2, constructorInstructions.Count(
            instruction => instruction.Opcode == WasmOpcodes.I32Store));
        Assert.Contains(normalInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.I32Constant &&
            instruction.Operand == WasmInstructionOperand.Signed(0));
        Assert.DoesNotContain(normalInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand == WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary));
        Assert.Contains(constructorInstructions, instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand == WasmInstructionOperand.Unsigned((uint)context.ObjectTemporary));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void SuccessiveDisjointOwnersPublishThroughTheSamePhysicalSlot(
        WasmTarget target)
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var slots = ImmutableDictionary<RootSource, int>.Empty
            .Add(argument, 0)
            .Add(local, 0);
        var emitter = CreateEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        var argumentMap = new MethodRootMap(
            EntryKey,
            slots,
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [argument], [])));
        var argumentContext = CreateMethodEmissionContext() with
        {
            RootMap = argumentMap,
            ParameterOffset = 4,
            LocalBase = 8,
        };
        var argumentRequest = CreateInstructionRequest(
            CilOperation.Call,
            context: argumentContext);
        emitter.Emit(argumentRequest, GetCodeWriter(argumentRequest));

        var localMap = argumentMap with
        {
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [local], [])),
        };
        var localContext = argumentContext with { RootMap = localMap };
        var localRequest = CreateInstructionRequest(CilOperation.Call, context: localContext);
        emitter.Emit(localRequest, GetCodeWriter(localRequest));

        var expectedStore = target == WasmTarget.Wasm64
            ? WasmOpcodes.I64Store
            : WasmOpcodes.I32Store;
        Assert.Single(GetCodeBytes(argumentRequest), value => value == expectedStore);
        Assert.Single(GetCodeBytes(localRequest), value => value == expectedStore);
        Assert.NotEqual(GetCodeBytes(argumentRequest), GetCodeBytes(localRequest));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublishesEveryRootSourceKindAtItsSlotOffset(WasmTarget target)
    {
        var argument = new RootSource(RootSourceKind.Argument, 0);
        var local = new RootSource(RootSourceKind.Local, 0);
        var spilledLocal = new RootSource(RootSourceKind.Local, 1);
        var evaluationStack = new RootSource(RootSourceKind.EvaluationStack, 0, 4);
        var directEvaluationStack = new RootSource(RootSourceKind.EvaluationStack, 1);
        var addressArgument = new RootSource(RootSourceKind.ManagedAddressArgument, 1);
        var addressLocal = new RootSource(RootSourceKind.ManagedAddressLocal, 1);
        var addressEvaluationStack = new RootSource(
            RootSourceKind.ManagedAddressEvaluationStack,
            1);
        var allocation = new RootSource(RootSourceKind.AllocationTemporary, 0);
        var sources = ImmutableArray.Create(
            argument,
            local,
            spilledLocal,
            evaluationStack,
            directEvaluationStack,
            addressArgument,
            addressLocal,
            addressEvaluationStack,
            allocation);
        var rootMap = new MethodRootMap(
            EntryKey,
            sources.Select((source, index) => (source, index)).ToImmutableDictionary(
                pair => pair.source,
                pair => pair.index),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, sources, [])));
        var context = CreateMethodEmissionContext() with
        {
            RootMap = rootMap,
            ValueLayout = new ValueFrameLayout(
                0,
                [],
                [],
                [],
                ImmutableHashSet<int>.Empty.Add(1)),
        };
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var request = CreateInstructionRequest(
            CilOperation.Call,
            context: context);
        var layout = WasmTargetLayout.For(target);
        var code = new RecordingInstructionWriter();

        CreateEmitter(new RecordingLayoutProvider(layout), filterRoots).Emit(request, code);

        var instructions = code.ToInstructions();
        var store = target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store;
        Assert.Equal(
            Enumerable.Range(0, sources.Length).Select(slot => (uint)(slot * layout.ObjectReferenceSize)),
            instructions.Where(instruction => instruction.Opcode == store)
                .Select(instruction => instruction.Operand.Offset));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.I32Add or WasmOpcodes.I64Add);
        Assert.Equal(1, filterRoots.Calls);
    }

    [Fact]
    public void ReturnsWithoutSafepointOrWhenRootSlotsAreAbsent()
    {
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var noSafepoint = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext());
        CreateEmitter(filterRoots: filterRoots).Emit(
            noSafepoint,
            GetCodeWriter(noSafepoint));

        var emptySlotsMap = new MethodRootMap(
            EntryKey,
            [],
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [], [])));
        var noSlots = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = emptySlotsMap });
        CreateEmitter(filterRoots: filterRoots).Emit(
            noSlots,
            GetCodeWriter(noSlots));

        Assert.Equal(0, filterRoots.Calls);
        Assert.Empty(GetCodeBytes(noSafepoint));
        Assert.Empty(GetCodeBytes(noSlots));
    }

    [Fact]
    public void ConstructorPublicationStopsAfterFilterEnvironmentWhenNoConstructorRootsExist()
    {
        var source = new RootSource(RootSourceKind.Argument, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [source], [])));
        var request = CreateInstructionRequest(
            CilOperation.NewObject,
            context: CreateMethodEmissionContext() with { RootMap = rootMap });
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();

        CreateEmitter(filterRoots: filterRoots).Emit(
            request,
            GetCodeWriter(request),
            constructorCall: true);

        Assert.Equal(1, filterRoots.Calls);
        Assert.Empty(GetCodeBytes(request));
    }

    [Fact]
    public void UnknownRootSourceIsRejectedDeterministically()
    {
        var unknown = new RootSource((RootSourceKind)99, 0);
        var rootMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty.Add(unknown, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [unknown], [unknown])));
        var request = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = rootMap });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter().Emit(request, GetCodeWriter(request)));

        Assert.Contains("Unknown root source", exception.Message);
    }

    [Fact]
    public void InvalidPhysicalAssignmentsFailBeforeAnyPublication()
    {
        var first = new RootSource(RootSourceKind.Argument, 0);
        var second = new RootSource(RootSourceKind.Local, 0);
        var filterRoots = new RecordingFilterEnvironmentRootEmitter();
        var emitter = CreateEmitter(filterRoots: filterRoots);

        var collisionMap = new MethodRootMap(
            EntryKey,
            ImmutableDictionary<RootSource, int>.Empty
                .Add(first, 0)
                .Add(second, 0),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [first, second], [])));
        var collision = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = collisionMap });

        Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(collision, GetCodeWriter(collision)));
        Assert.Empty(GetCodeBytes(collision));
        Assert.Equal(0, filterRoots.Calls);

        var missingMap = collisionMap with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(first, 0),
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(
                0,
                new SafepointRootMap(0, [second], [])),
        };
        var missing = CreateInstructionRequest(
            CilOperation.Call,
            context: CreateMethodEmissionContext() with { RootMap = missingMap });

        Assert.Throws<InvalidOperationException>(() =>
            emitter.Emit(missing, GetCodeWriter(missing)));
        Assert.Empty(GetCodeBytes(missing));
        Assert.Equal(0, filterRoots.Calls);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PromotedAggregateLanesPublishTheirLocalInsteadOfTheStaleFrame(WasmTarget target)
    {
        var argument = new RootSource(RootSourceKind.Argument, 1, 0);
        var local = new RootSource(RootSourceKind.Local, 2, 8);
        var unpromoted = new RootSource(RootSourceKind.Local, 3, 0);
        var direct = new RootSource(RootSourceKind.Local, 2);
        var stack = new RootSource(RootSourceKind.EvaluationStack, 0, 0);
        RootSource[] roots = [argument, local, unpromoted, direct, stack];
        var argumentStorage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Argument, 1);
        var localStorage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Local, 2);
        var context = CreateMethodEmissionContext() with
        {
            RootMap = new(EntryKey,
                roots.Select((source, slot) => (source, slot)).ToImmutableDictionary(pair => pair.source, pair => pair.slot),
                ImmutableDictionary<int, SafepointRootMap>.Empty.Add(0, new(0, [.. roots], []))),
            ManagedAggregateValues = new(ManagedAggregateSsaPlan.Empty, ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty
                .Add(new(argumentStorage, 0), 40)
                .Add(new(localStorage, 8), 41)),
        };
        var code = new RecordingInstructionWriter();

        CreateEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)))
            .Emit(CreateInstructionRequest(CilOperation.Call, context: context), code);

        // Each slot store follows the value it publishes; the promoted lanes are read directly.
        var instructions = code.ToInstructions();
        var store = target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store;
        var published = instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Opcode == store)
            .Select(pair => instructions[pair.index - 1])
            .ToArray();
        Assert.Equal(5, published.Length);
        Assert.Equal((WasmOpcodes.LocalGet, 40u), (published[0].Opcode, published[0].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.LocalGet, 41u), (published[1].Opcode, published[1].Operand.UnsignedValue));
        Assert.NotEqual(WasmOpcodes.LocalGet, published[2].Opcode);
    }

    private static IRootPublicationEmitter CreateEmitter(
        RecordingLayoutProvider? layouts = null,
        RecordingFilterEnvironmentRootEmitter? filterRoots = null)
    {
        layouts ??= new RecordingLayoutProvider();
        filterRoots ??= new RecordingFilterEnvironmentRootEmitter();
        return new[]
        {
            new RootPublicationEmitter(layouts, filterRoots, CreateValueFrameAddresses(layouts),
                new StackStorageAnchorValueEmitter(layouts, CreateValueFrameAddresses(layouts))),
        }.Cast<IRootPublicationEmitter>().Single();
    }

    private static void EmitThroughCapability(
        IRootPublicationEmitter emitter,
        InstructionEmissionRequest request,
        IWasmInstructionWriter code,
        bool constructorCall = false) =>
        emitter.Emit(request, code, constructorCall);

    private sealed class RecordingFilterEnvironmentRootEmitter :
        IFilterEnvironmentRootEmitter
    {
        public int Calls { get; private set; }

        public void Emit(IWasmInstructionWriter code, MethodEmissionContext context) => Calls++;
    }
}
