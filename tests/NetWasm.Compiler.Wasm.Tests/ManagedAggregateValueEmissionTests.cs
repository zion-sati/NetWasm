// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Values;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ManagedAggregateValueEmissionTests
{
    private static readonly ManagedAggregateStorageId Storage = new(
        ManagedAggregateStorageKind.Argument,
        0);
    private static readonly ManagedAggregateSsaLane Low = new(Storage, 0);
    private static readonly ManagedAggregateSsaLane High = new(Storage, sizeof(int));

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void ArgumentInitializerRejectsMissingBackingBeforeEmission(WasmTarget target)
    {
        var context = Context(Plan());
        context = context with { ValueLayout = context.ValueLayout with { ArgumentOffsets = [] } };
        var writer = new RecordingInstructionWriter();
        var initializer = new ManagedAggregateValueInitializer(new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        Assert.Throws<InvalidOperationException>(() => initializer.TryInitializeArgument(writer, Header(), context, 0));

        Assert.Empty(writer.ToInstructions());
    }

    [Fact]
    public void ArgumentInitializerRejectsMissingDependenciesAndInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ManagedAggregateValueInitializer(null!));
        var initializer = new ManagedAggregateValueInitializer(new RecordingLayoutProvider());
        var writer = new RecordingInstructionWriter();
        var context = Context(Plan());
        var header = Header();
        Assert.Throws<ArgumentNullException>(() => initializer.TryInitializeArgument(null!, header, context, 0));
        Assert.Throws<ArgumentNullException>(() => initializer.TryInitializeArgument(writer, null!, context, 0));
        Assert.Throws<ArgumentNullException>(() => initializer.TryInitializeArgument(writer, header, null!, 0));
        Assert.Empty(writer.ToInstructions());
    }

    [Fact]
    public void InstructionEmitterRejectsMissingDependenciesAndInputs()
    {
        var layouts = new RecordingLayoutProvider();
        var addresses = new RecordingValueFrameAddressEmitter();
        Assert.Throws<ArgumentNullException>(() => new ManagedAggregateValueInstructionEmitter(null!, addresses));
        Assert.Throws<ArgumentNullException>(() => new ManagedAggregateValueInstructionEmitter(layouts, null!));
        var emitter = new ManagedAggregateValueInstructionEmitter(layouts, addresses);
        var writer = new RecordingInstructionWriter();
        Assert.Throws<ArgumentNullException>(() => emitter.TryEmit(null!, writer));
        Assert.Throws<ArgumentNullException>(() => emitter.TryEmit(CreateInstructionRequest(CilOperation.Nop), null!));
        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
    }

    [Theory]
    [InlineData(CliValueKind.F4, 4, false)]
    [InlineData(CliValueKind.I4, 8, false)]
    [InlineData(CliValueKind.I8, 4, false)]
    [InlineData(CliValueKind.I8, 8, false)]
    [InlineData(CliValueKind.I4, 4, true)]
    public void InstructionEmitterRejectsUnsupportedLaneShapes(CliValueKind kind, int size, bool alsoWrites)
    {
        var access = new ManagedAggregateSsaAccess(new(10, new(0),
            new(Storage, 0, CliTypeIdentity.FromStackKind(kind)), size, kind, false),
            [new(Low, new(0))], alsoWrites ? [new(Low, new(1))] : []);
        var plan = Plan() with { Accesses = ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty.Add(10, access) };
        var request = new InstructionEmissionRequest(Header(), I(10, CilOperation.LoadObject),
            [CliValueKind.ManagedAddress], Context(plan), CreateInstructionModuleTarget());
        var writer = new RecordingInstructionWriter();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = new ManagedAggregateValueInstructionEmitter(new RecordingLayoutProvider(), addresses);

        Assert.Throws<InvalidOperationException>(() => emitter.TryEmit(request, writer));

        Assert.Empty(writer.ToInstructions());
        Assert.Empty(addresses.Offsets);
        Assert.Equal(CliValueKind.ManagedAddress, Assert.Single(request.Stack));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void LocalMaterializationRequiresAndUsesLocalValueFrameBacking(WasmTarget target, bool hasBacking)
    {
        var storage = new ManagedAggregateStorageId(ManagedAggregateStorageKind.Local, 0);
        var lane = new ManagedAggregateSsaLane(storage, 0);
        var plan = new ManagedAggregateSsaPlan(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(storage,
                new(storage, CliTypeIdentity.FromStackKind(CliValueKind.ValueType), 4, true)),
            [lane], ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>.Empty.Add(lane, new(0)),
            [], [], [], [], [new(new(30, storage), [new(lane, new(0))])], []);
        var context = Context(plan);
        context = context with
        {
            ValueLayout = context.ValueLayout with
            {
                LocalOffsets = hasBacking
                ? ImmutableDictionary<int, int>.Empty.Add(0, 24) : []
            },
            ManagedAggregateValues = new(plan, ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty.Add(lane, 40)),
        };
        var request = new InstructionEmissionRequest(Header(), I(30, CilOperation.Nop), [], context,
            CreateInstructionModuleTarget());
        var writer = new RecordingInstructionWriter();
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = new ManagedAggregateValueInstructionEmitter(new RecordingLayoutProvider(WasmTargetLayout.For(target)), addresses);

        if (hasBacking)
        {
            Assert.False(emitter.TryEmit(request, writer));
            Assert.Equal(24, Assert.Single(addresses.Offsets));
            Assert.Equal(40u, writer.ToInstructions()[0].Operand.UnsignedValue);
            Assert.Equal(WasmOpcodes.I32Store, writer.ToInstructions()[1].Opcode);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => emitter.TryEmit(request, writer));
            Assert.Empty(writer.ToInstructions());
            Assert.Empty(addresses.Offsets);
        }
        Assert.Empty(request.Stack);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void JoinsAndSplitsBothIntegerLanesForWideAccesses(WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = new ManagedAggregateValueInstructionEmitter(
            layouts, new RecordingValueFrameAddressEmitter());
        var plan = Plan() with
        {
            Accesses = ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty
                .Add(10, new(new(10, new(0),
                    new(Storage, 0, CliTypeIdentity.FromStackKind(CliValueKind.I8)),
                    8, CliValueKind.I8, false), [new(Low, new(0)), new(High, new(1))], []))
                .Add(20, new(new(20, new(0),
                    new(Storage, 0, CliTypeIdentity.FromStackKind(CliValueKind.I8)),
                    8, CliValueKind.I8, true), [], [new(Low, new(2)), new(High, new(3))])),
        };
        var context = Context(plan);
        var targetPlan = CreateInstructionModuleTarget(new FakeProgram());
        var loadStack = new List<CliValueKind> { CliValueKind.ManagedAddress };
        var loadCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(Header(), I(10, CilOperation.LoadObject),
            loadStack, context, targetPlan), loadCode));
        Assert.Equal([CliValueKind.I8], loadStack);
        Assert.Equal(new[]
        {
            WasmOpcodes.LocalGet, WasmOpcodes.I64ExtendI32Unsigned,
            WasmOpcodes.LocalGet, WasmOpcodes.I64ExtendI32Unsigned,
            WasmOpcodes.I64Constant, WasmOpcodes.I64ShiftLeft,
            WasmOpcodes.I64Or, WasmOpcodes.LocalSet,
        }, loadCode.ToInstructions().Select(instruction => instruction.Opcode));
        Assert.Equal(30u, loadCode.ToInstructions()[0].Operand.UnsignedValue);
        Assert.Equal(31u, loadCode.ToInstructions()[2].Operand.UnsignedValue);
        var storeStack = new List<CliValueKind> { CliValueKind.ManagedAddress, CliValueKind.I8 };
        var storeCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(Header(), I(20, CilOperation.StoreObject),
            storeStack, context, targetPlan), storeCode));
        Assert.Empty(storeStack);
        Assert.Equal(new[]
        {
            WasmOpcodes.LocalGet, WasmOpcodes.I32WrapI64, WasmOpcodes.LocalSet,
            WasmOpcodes.LocalGet, WasmOpcodes.I64Constant, WasmOpcodes.I64ShiftRightUnsigned,
            WasmOpcodes.I32WrapI64, WasmOpcodes.LocalSet,
        }, storeCode.ToInstructions().Select(instruction => instruction.Opcode));
        Assert.Equal(30u, storeCode.ToInstructions()[2].Operand.UnsignedValue);
        Assert.Equal(31u, storeCode.ToInstructions()[7].Operand.UnsignedValue);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void InitializesSelectedArgumentLanesFromTheOriginalParameter(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var plan = Plan();
        var context = Context(plan) with { ParameterOffset = 1 };
        var writer = new RecordingInstructionWriter();
        var initializer = new ManagedAggregateValueInitializer(layouts);
        var header = Header();

        Assert.True(initializer.TryInitializeArgument(writer, header, context, 0));
        Assert.False(initializer.TryInitializeArgument(writer, header, context, 1));

        var instructions = writer.ToInstructions();
        Assert.Equal(6, instructions.Length);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)), instructions[0]);
        Assert.Equal(WasmOpcodes.I32Load, instructions[1].Opcode);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(30)), instructions[2]);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)), instructions[3]);
        Assert.Equal(WasmOpcodes.I32Load, instructions[4].Opcode);
        Assert.Equal(4u, instructions[4].Operand.Offset);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(31)), instructions[5]);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void RewritesLaneLoadsStoresAndFlushesMaterializations(
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = new ManagedAggregateValueInstructionEmitter(layouts, addresses);
        var plan = Plan(withOperations: true);
        var context = Context(plan);
        var program = new FakeProgram();
        var targetPlan = CreateInstructionModuleTarget(program);

        var loadStack = new List<CliValueKind> { CliValueKind.ManagedAddress };
        var loadCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(
            Header(),
            I(10, CilOperation.LoadField),
            loadStack,
            context,
            targetPlan), loadCode));
        Assert.Equal([CliValueKind.I4], loadStack);
        Assert.Equal(WasmOpcodes.LocalGet, loadCode.ToInstructions()[0].Opcode);
        Assert.Equal(30u, loadCode.ToInstructions()[0].Operand.UnsignedValue);

        var storeStack = new List<CliValueKind>
        {
            CliValueKind.ManagedAddress,
            CliValueKind.I4,
        };
        var storeCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(
            Header(),
            I(20, CilOperation.StoreField),
            storeStack,
            context,
            targetPlan), storeCode));
        Assert.Empty(storeStack);
        Assert.Equal(WasmOpcodes.LocalSet, storeCode.ToInstructions()[1].Opcode);
        Assert.Equal(31u, storeCode.ToInstructions()[1].Operand.UnsignedValue);

        var flushCode = new RecordingInstructionWriter();
        Assert.False(emitter.TryEmit(new(
            Header(),
            I(30, CilOperation.Nop),
            [],
            context,
            targetPlan), flushCode));
        Assert.Equal([12, 16], addresses.Offsets);
        Assert.Equal(2, flushCode.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32Store));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MovesReferenceLanesWholeAndClearsEveryLane(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var pointer = layout.ObjectReferenceSize;
        var reference = new ManagedAggregateSsaLane(Storage, 0);
        var integer = new ManagedAggregateSsaLane(Storage, pointer);
        var type = CliTypeIdentity.FromStackKind(CliValueKind.ValueType);
        var recipe = new ManagedAggregateAddressRecipe(Storage, 0, type);
        var storage = new ManagedAggregateStorage(Storage, type, pointer + sizeof(int), true)
        {
            LaneShapes = [new(0, pointer, CliValueKind.ManagedAddress), new(pointer, sizeof(int), CliValueKind.I4)],
        };
        var plan = Plan() with
        {
            Storages = ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>.Empty.Add(Storage, storage),
            Lanes = [reference, integer],
            Accesses = ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty
                .Add(10, new(new(10, new(0), recipe, pointer, CliValueKind.ManagedAddress, false) { LaneOffsets = [0] },
                    [new(reference, new(0))], []))
                .Add(20, new(new(20, new(0), recipe, pointer, CliValueKind.ManagedAddress, true) { LaneOffsets = [0] },
                    [], [new(reference, new(1))]))
                .Add(30, new(new(30, new(0), recipe, pointer + sizeof(int), CliValueKind.ValueType, true)
                {
                    LaneOffsets = [0, pointer],
                },
                    [], [new(reference, new(2)), new(integer, new(3))])),
            Materializations = [new(new(40, Storage), [new(reference, new(2)), new(integer, new(3))])],
        };
        var context = Context(plan) with
        {
            ManagedAggregateValues = new(plan,
                ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty.Add(reference, 30).Add(integer, 31)),
        };
        var addresses = new RecordingValueFrameAddressEmitter();
        var emitter = new ManagedAggregateValueInstructionEmitter(new RecordingLayoutProvider(layout), addresses);
        var targetPlan = CreateInstructionModuleTarget(new FakeProgram());

        var loadStack = new List<CliValueKind> { CliValueKind.ManagedAddress };
        var loadCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(Header(), I(10, CilOperation.LoadField), loadStack, context, targetPlan), loadCode));
        Assert.Equal([CliValueKind.ManagedAddress], loadStack);
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.LocalSet], loadCode.ToInstructions().Select(instruction => instruction.Opcode));
        Assert.Equal(30u, loadCode.ToInstructions()[0].Operand.UnsignedValue);

        var storeStack = new List<CliValueKind> { CliValueKind.ManagedAddress, CliValueKind.ManagedAddress };
        var storeCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(Header(), I(20, CilOperation.StoreField), storeStack, context, targetPlan), storeCode));
        Assert.Empty(storeStack);
        Assert.Equal([WasmOpcodes.LocalGet, WasmOpcodes.LocalSet], storeCode.ToInstructions().Select(instruction => instruction.Opcode));
        Assert.Equal(30u, storeCode.ToInstructions()[1].Operand.UnsignedValue);

        var clearStack = new List<CliValueKind> { CliValueKind.ManagedAddress };
        var clearCode = new RecordingInstructionWriter();
        Assert.True(emitter.TryEmit(new(Header(), I(30, CilOperation.InitializeObject), clearStack, context, targetPlan), clearCode));
        Assert.Empty(clearStack);
        Assert.Equal(
            [
                target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant, WasmOpcodes.LocalSet,
                WasmOpcodes.I32Constant, WasmOpcodes.LocalSet,
            ],
            clearCode.ToInstructions().Select(instruction => instruction.Opcode));
        Assert.Equal([30u, 31u], clearCode.ToInstructions().Where(instruction => instruction.Opcode == WasmOpcodes.LocalSet)
            .Select(instruction => instruction.Operand.UnsignedValue));

        var flushCode = new RecordingInstructionWriter();
        Assert.False(emitter.TryEmit(new(Header(), I(40, CilOperation.Call), [], context, targetPlan), flushCode));
        Assert.Equal([12, 12 + pointer], addresses.Offsets);
        Assert.Equal(
            [target == WasmTarget.Wasm64 ? WasmOpcodes.I64Store : WasmOpcodes.I32Store, WasmOpcodes.I32Store],
            flushCode.ToInstructions().Select(instruction => instruction.Opcode)
                .Where(opcode => opcode is WasmOpcodes.I32Store or WasmOpcodes.I64Store));
    }

    private static ManagedAggregateSsaPlan Plan(bool withOperations = false)
    {
        var storage = new ManagedAggregateStorage(
            Storage,
            CliTypeIdentity.FromStackKind(CliValueKind.ValueType),
            2 * sizeof(int),
            true);
        var accesses = withOperations
            ? ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty
                .Add(10, new(
                    new(10, new(0), new(Storage, 0,
                        CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                        sizeof(int), CliValueKind.I4, Writes: false),
                    [new(Low, new(0))],
                    []))
                .Add(20, new(
                    new(20, new(0), new(Storage, sizeof(int),
                        CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                        sizeof(int), CliValueKind.I4, Writes: true),
                    [],
                    [new(High, new(2))]))
            : ImmutableDictionary<int, ManagedAggregateSsaAccess>.Empty;
        var materializations = withOperations
            ? ImmutableArray.Create(new ManagedAggregateSsaMaterialization(
                new(30, Storage),
                [new(Low, new(0)), new(High, new(2))]))
            : [];
        return new(
            ImmutableDictionary<ManagedAggregateStorageId, ManagedAggregateStorage>
                .Empty.Add(Storage, storage),
            [Low, High],
            ImmutableDictionary<ManagedAggregateSsaLane, ManagedAggregateSsaValue>
                .Empty.Add(Low, new(0)).Add(High, new(1)),
            [],
            accesses,
            [],
            [],
            materializations,
            []);
    }

    private static MethodEmissionContext Context(ManagedAggregateSsaPlan plan) =>
        CreateMethodEmissionContext() with
        {
            ValueLayout = new(
                20,
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, 12),
                [],
                []),
            ManagedAggregateValues = new(
                plan,
                ImmutableDictionary<ManagedAggregateSsaLane, int>.Empty
                    .Add(Low, 30)
                    .Add(High, 31)),
        };

    private static StructuredMethodHeader Header() => new(
        new FakeProgram().GetMethod(EntryKey),
        null,
        1,
        [],
        [],
        []);

    private sealed class RecordingValueFrameAddressEmitter :
        IValueFrameAddressEmitter
    {
        public List<int> Offsets { get; } = [];

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            int offset) => Offsets.Add(offset);

        public void Emit(
            IWasmInstructionWriter code,
            MethodEmissionContext context,
            FilterCapture capture) => Offsets.Add(capture.Offset);
    }
}
