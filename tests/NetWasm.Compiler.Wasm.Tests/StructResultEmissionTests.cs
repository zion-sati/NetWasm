// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.StructResults;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StructResultEmissionTests
{
    private static readonly AssemblyIdentity Assembly = new("StructResultEmission");
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int16 = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
    private static readonly CliTypeIdentity Int64 = CliTypeIdentity.Primitive("i8", CliValueKind.I8);
    private static readonly CliTypeIdentity Object = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity Task = CliTypeIdentity.Named(Assembly, "Fixture", "Task", true);
    private static readonly CliTypeIdentity Pair = CliTypeIdentity.Named(Assembly, "Fixture", "Pair", true);
    private static readonly CliTypeIdentity Wide = CliTypeIdentity.Named(Assembly, "Fixture", "Wide", true);
    private static readonly CliTypeIdentity Kept = CliTypeIdentity.Named(Assembly, "Fixture", "Kept", true);

    // Task: a reference, an int, a short and two bytes of padding; Pair: two ints; Wide: a long and a reference; Kept:
    // through a buffer.
    private static readonly Dictionary<CliTypeIdentity, StructResultShape> Shapes = new()
    {
        [Task] = new(Task, [new(0, 4, Object, CliValueKind.ManagedReference), new(4, 4, Int32, CliValueKind.I4),
            new(8, 2, Int16, CliValueKind.I4)], 12),
        [Pair] = new(Pair, [new(0, 4, Int32, CliValueKind.I4), new(4, 4, Int32, CliValueKind.I4)], 8),
        [Wide] = new(Wide, [new(0, 8, Int64, CliValueKind.I8), new(8, 4, Object, CliValueKind.ManagedReference)], 16),
    };

    // The method's own return and every managed call's count; a kind takes the most any one shape holds, so Task's
    // two I4 fields and Pair's two share two I4 locals. An unmanaged calli and a value kept in a buffer need none.
    [Fact]
    public void TheScratchPlanTakesTheMostOfEachKindAnyOneShapeHolds()
    {
        var header = new StructuredMethodHeader(Method(Task), null, 4, [], [],
        [
            Call(0, Pair),
            Call(5, Wide, CilOperation.CallVirtual),
            new(10, 15, CilOperation.CallIndirect, new CilOperand.CallSite(new(Kept, []))),
            new(15, 20, CilOperation.CallIndirect, new CilOperand.CallSite(new(Pair, []), Unmanaged: true)),
            Call(20, Int32),
        ]);

        var kinds = new StructResultScratchPlanner(new Classifier()).Plan(header);

        Assert.Equal(
            [CliValueKind.I4, CliValueKind.I4, CliValueKind.I8, CliValueKind.ManagedReference],
            kinds.AsEnumerable());
        Assert.Empty(new StructResultScratchPlanner(new NullStructResultClassifier()).Plan(header));
    }

    [Fact]
    public void ALocalPerFieldIsTakenInFieldOrderByKind()
    {
        var locals = new StructResultLocals(new Dictionary<CliValueKind, ImmutableArray<int>>
        {
            [CliValueKind.I4] = [7, 8],
            [CliValueKind.ManagedReference] = [9],
        }.ToImmutableDictionary());

        Assert.Equal([9, 7, 8], locals.For(Shapes[Task]).AsEnumerable());
        Assert.Throws<InvalidOperationException>(() => locals.For(Shapes[Wide]));
    }

    // A load reads each field from the address at its offset as a field load of its type and size does; a store takes
    // the results off the stack last first, stores each at its offset, then writes the padding the fields leave as
    // zeros (Task's two bytes after its short), as a whole copy of a value read from zeroed memory would.
    [Fact]
    public void LoadsAndStoresMoveEachFieldAsAFieldAccessDoes()
    {
        var emitter = new StructResultEmitter(new Layouts());
        var load = new EmitterTestSupport.RecordingInstructionWriter();
        emitter.Emit(load, Shapes[Task], Address);
        Assert.Equal(
        [
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Load, 0u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Load, 4u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Load16Signed, 8u),
        ], Summary(load));

        var store = new EmitterTestSupport.RecordingInstructionWriter();
        emitter.Emit(store, Shapes[Task], [9, 7, 8], Address);
        Assert.Equal(
        [
            ((byte)WasmOpcodes.LocalSet, 8u), ((byte)WasmOpcodes.LocalSet, 7u), ((byte)WasmOpcodes.LocalSet, 9u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.LocalGet, 9u), ((byte)WasmOpcodes.I32Store, 0u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.LocalGet, 7u), ((byte)WasmOpcodes.I32Store, 4u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.LocalGet, 8u), ((byte)WasmOpcodes.I32Store16, 8u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Constant, 0u), ((byte)WasmOpcodes.I32Store16, 10u),
        ], Summary(store));
        Assert.Throws<ArgumentException>(() => emitter.Emit(store, Shapes[Task], [9, 7], Address));
    }

    // A result through a buffer: its address before the arguments and the call. In results: the call, then each field
    // taken into the locals the sequence names and stored at the destination, or, kept in lanes, only taken.
    [Fact]
    public void AResultConventionPutsTheDestinationOnTheSideItTakesIt()
    {
        var conventions = new ValueTypeResultConventionFactory(new Classifier(), new StructResultEmitter(new Layouts()));
        var buffer = conventions.Create(Kept);
        Assert.Empty(buffer.FieldLocalKinds);
        var buffered = new EmitterTestSupport.RecordingInstructionWriter();
        buffer.Emit(buffered, Address, new(StructResultLocals.Empty), Call);
        Assert.Equal([((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.Call, 42u)], Summary(buffered));

        var fields = conventions.Create(Pair);
        Assert.Equal([CliValueKind.I4, CliValueKind.I4], fields.FieldLocalKinds.AsEnumerable());
        var locals = StructResultLocals.Sequential(7, fields.FieldLocalKinds);
        var stored = new EmitterTestSupport.RecordingInstructionWriter();
        fields.Emit(stored, Address, new(locals), Call);
        Assert.Equal(
        [
            ((byte)WasmOpcodes.Call, 42u), ((byte)WasmOpcodes.LocalSet, 8u), ((byte)WasmOpcodes.LocalSet, 7u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.LocalGet, 7u), ((byte)WasmOpcodes.I32Store, 0u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.LocalGet, 8u), ((byte)WasmOpcodes.I32Store, 4u),
        ], Summary(stored));
        var kept = new EmitterTestSupport.RecordingInstructionWriter();
        fields.Emit(kept, Address, new(locals, InLanes: true), Call);
        Assert.Equal(
            [((byte)WasmOpcodes.Call, 42u), ((byte)WasmOpcodes.LocalSet, 8u), ((byte)WasmOpcodes.LocalSet, 7u)],
            Summary(kept));
        Assert.Same(BufferResultConvention.Instance, new BufferResultConventionFactory().Create(Pair));
    }

    // An argument for a parameter of no classified struct type goes as the stack holds it; a classified struct's fields
    // are read through the address the stack holds.
    [Fact]
    public void AnArgumentConventionPassesTheStackValueOrTheStructsFields()
    {
        var conventions = new ValueTypeArgumentConventionFactory(new Classifier(), new StructResultEmitter(new Layouts()));
        foreach (var parameter in new CliTypeIdentity?[] { null, Int32, Kept })
        {
            var plain = new EmitterTestSupport.RecordingInstructionWriter();
            conventions.Create(parameter).Emit(plain, Address, new());
            Assert.Equal([((byte)WasmOpcodes.LocalGet, 3u)], Summary(plain));
        }
        var fields = new EmitterTestSupport.RecordingInstructionWriter();
        conventions.Create(Pair).Emit(fields, Address, new());
        Assert.Equal(
        [
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Load, 0u),
            ((byte)WasmOpcodes.LocalGet, 3u), ((byte)WasmOpcodes.I32Load, 4u),
        ], Summary(fields));
        Assert.Same(StackArgumentConvention.Instance, new StackArgumentConventionFactory().Create(Pair));
    }

    // Locals numbered from the first in the order of the kinds given come back in that order for a shape of those kinds.
    [Fact]
    public void SequentialLocalsComeBackInFieldOrder()
    {
        var locals = StructResultLocals.Sequential(5, [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);
        Assert.Equal([5, 6, 7], locals.For(Shapes[Task]).AsEnumerable());
    }

    private static void Call(IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand((byte)WasmOpcodes.Call, WasmInstructionOperand.Unsigned(42)));

    private static void Address(IWasmInstructionWriter code) =>
        code.Write(WasmInstruction.WithOperand((byte)WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(3)));

    // Each instruction's opcode with its local index or memory offset.
    private static (byte, uint)[] Summary(EmitterTestSupport.RecordingInstructionWriter writer) =>
        [.. writer.ToInstructions().Select(static instruction => (instruction.Opcode,
            instruction.Operand.Shape == WasmInstructionOperandShape.MemoryArgument
                ? (uint)instruction.Operand.Offset
                : instruction.Operand.UnsignedValue))];

    private static CilInstruction Call(int offset, CliTypeIdentity returned, CilOperation operation = CilOperation.Call) =>
        new(offset, offset + 5, operation, new CilOperand.MethodInstance(new(Method(returned), returned, [], new(returned, []))));

    private static MethodDefinitionModel Method(CliTypeIdentity returned) =>
        new(new(Assembly, 1), new(Assembly, 2), "Method", true, new(returned, []), 1);

    private sealed class Layouts : ITargetLayout
    {
        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;
    }

    private sealed class Classifier : IStructResultClassifier
    {
        public StructResultShape? Classify(CliTypeIdentity type) => Shapes.GetValueOrDefault(type);
    }
}
