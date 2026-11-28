// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

// The steps of a lazily entered root frame: entered (zeroed, authoritative slots copied in) only while its local is 0,
// left only once entered, and the local saved before a protected block and restored at its landing pad.
public sealed class LazyRootFrameEmitterTests
{
    private static readonly StructuredExceptionGroupId Group = new(0);

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EnsureEntersAndZeroesTheFrameOnlyWhileItsLocalIsZero(WasmTarget target)
    {
        var (emitter, imports, layout) = Create(target);
        var context = Context(slots: 2);
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Ensure, context);

        var instructions = code.ToInstructions();
        Assert.Equal((WasmOpcodes.LocalGet, (uint)context.RootFrame), (instructions[0].Opcode, instructions[0].Operand.UnsignedValue));
        Assert.Equal(layout.UsesMemory64 ? WasmOpcodes.I64EqualZero : WasmOpcodes.I32EqualZero, instructions[1].Opcode);
        Assert.Equal(WasmOpcodes.If, instructions[2].Opcode);
        Assert.Equal((WasmOpcodes.I32Constant, 2), (instructions[3].Opcode, instructions[3].Operand.SignedValue));
        Assert.Equal((WasmOpcodes.Call, (uint)imports.Resolve(RuntimeImportSymbol.RootFrameEnter)),
            (instructions[4].Opcode, instructions[4].Operand.UnsignedValue));
        Assert.Equal((WasmOpcodes.LocalSet, (uint)context.RootFrame), (instructions[5].Opcode, instructions[5].Operand.UnsignedValue));
        var zeroing = new RecordingInstructionWriter();
        FrameRangeZeroing.Emit(zeroing, layout, writer => writer.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned((uint)context.RootFrame))), 2 * layout.ObjectReferenceSize);
        Assert.Equal(zeroing.ToInstructions().Select(static instruction => instruction.ToString()),
            instructions[6..^1].Select(static instruction => instruction.ToString()));
        Assert.Equal(WasmOpcodes.End, instructions[^1].Opcode);
    }

    // A store made while the frame was absent wrote no slot, so the entry copies each authoritative variable in.
    [Fact]
    public void EnsureCopiesEachAuthoritativeVariableIntoItsSlot()
    {
        var (emitter, _, layout) = Create(WasmTarget.Wasm32);
        var argument = new RootSource(RootSourceKind.Argument, 1);
        var local = new RootSource(RootSourceKind.Local, 0);
        var context = Context(slots: 3) with
        {
            AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty.Add(local, 2).Add(argument, 1),
        };
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Ensure, context);

        var instructions = code.ToInstructions();
        var copies = instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Opcode == WasmOpcodes.I32Store && pair.index >= 2 &&
                instructions[pair.index - 2].Opcode == WasmOpcodes.LocalGet &&
                instructions[pair.index - 1].Opcode == WasmOpcodes.LocalGet)
            .Select(pair => (instructions[pair.index - 1].Operand.UnsignedValue, pair.instruction.Operand.Offset))
            .ToArray();
        Assert.Equal([((uint)(1 + context.ParameterOffset), 4u), ((uint)context.LocalBase, 8u)], copies);
    }

    // The publication that entered the frame stores its own slots, and an authoritative slot takes its variable's copy,
    // both before anything can collect, so the entry zeroes only the rest.
    [Fact]
    public void EnsureZeroesOnlyWhatNeitherThePublicationNorACopyWrites()
    {
        var (emitter, _, _) = Create(WasmTarget.Wasm32);
        var context = Context(slots: 3);
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Ensure, context, publishedSlots: new HashSet<int> { 0, 1 });

        Assert.Equal([(WasmOpcodes.I32Constant, 8u)], Stores(code.ToInstructions()));

        var argument = new RootSource(RootSourceKind.Argument, 0);
        var copied = context with { AuthoritativeRootSlots = ImmutableDictionary<RootSource, int>.Empty.Add(argument, 2) };
        code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Ensure, copied, publishedSlots: new HashSet<int> { 0, 1 });

        Assert.Equal([(WasmOpcodes.LocalGet, 8u)], Stores(code.ToInstructions()));
    }

    // Each store's offset, with the instruction that pushes its value.
    private static (byte Value, uint Offset)[] Stores(ImmutableArray<WasmInstruction> instructions) => instructions
        .Select((instruction, index) => (instruction, index))
        .Where(pair => pair.instruction.Opcode is WasmOpcodes.I32Store or WasmOpcodes.I64Store ||
            pair.instruction is { Opcode: WasmOpcodes.SimdPrefixed, Operand.Alignment: WasmSimdOpcodes.V128Store })
        .Select(pair => (instructions[pair.index - 1].Opcode, pair.instruction.Opcode == WasmOpcodes.SimdPrefixed
            ? pair.instruction.Operand.Third : pair.instruction.Operand.Offset))
        .ToArray();

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void LeaveLeavesOnlyAnEnteredFrame(WasmTarget target)
    {
        var (emitter, imports, layout) = Create(target);
        var context = Context(slots: 1);
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Leave, context);

        var expected = new List<(byte, uint)> { (WasmOpcodes.LocalGet, (uint)context.RootFrame) };
        if (layout.UsesMemory64) expected.AddRange([(WasmOpcodes.I64EqualZero, 0u), (WasmOpcodes.I32EqualZero, 0u)]);
        expected.AddRange([(WasmOpcodes.If, 0u), (WasmOpcodes.LocalGet, (uint)context.RootFrame),
            (WasmOpcodes.Call, (uint)imports.Resolve(RuntimeImportSymbol.RootFrameLeave)), (WasmOpcodes.End, 0u)]);
        Assert.Equal(expected, code.ToInstructions().Select(static instruction =>
            (instruction.Opcode, instruction.Opcode == WasmOpcodes.If ? 0u : instruction.Operand.UnsignedValue)));
    }

    [Fact]
    public void SaveAndRestoreCopyTheLocalThroughTheBlocksOwn()
    {
        var (emitter, _, _) = Create(WasmTarget.Wasm32);
        var context = Context(slots: 1);
        var save = context.RootFrameSaves[Group];
        var code = new RecordingInstructionWriter();

        emitter.Emit(code, LazyRootFrameStep.Save, context, Group);
        emitter.Emit(code, LazyRootFrameStep.Restore, context, Group);

        Assert.Equal(
            [(WasmOpcodes.LocalGet, (uint)context.RootFrame), (WasmOpcodes.LocalSet, (uint)save),
             (WasmOpcodes.LocalGet, (uint)save), (WasmOpcodes.LocalSet, (uint)context.RootFrame)],
            code.ToInstructions().Select(static instruction => (instruction.Opcode, instruction.Operand.UnsignedValue)));
    }

    [Fact]
    public void RejectsMissingArgumentsAndUnknownSteps()
    {
        var (emitter, imports, _) = Create(WasmTarget.Wasm32);
        var context = Context(slots: 1);
        var code = new RecordingInstructionWriter();

        Assert.Throws<ArgumentNullException>(() => new LazyRootFrameEmitter(null!, imports));
        Assert.Throws<ArgumentNullException>(() => new LazyRootFrameEmitter(new RecordingLayoutProvider(), null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(null!, LazyRootFrameStep.Test, context));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, LazyRootFrameStep.Test, null!));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, LazyRootFrameStep.Save, context));
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(code, LazyRootFrameStep.Restore, context));
        Assert.Throws<ArgumentOutOfRangeException>(() => emitter.Emit(code, (LazyRootFrameStep)99, context));
    }

    private static (LazyRootFrameEmitter Emitter, RuntimeImportCatalog Imports, WasmTargetLayout Layout) Create(WasmTarget target)
    {
        var layout = WasmTargetLayout.For(target);
        var imports = WasmRuntimeImports.CreateCatalog();
        return (new LazyRootFrameEmitter(new RecordingLayoutProvider(layout), imports), imports, layout);
    }

    private static MethodEmissionContext Context(int slots) => CreateMethodEmissionContext() with
    {
        RootMap = new(EntryKey,
            Enumerable.Range(0, slots).ToImmutableDictionary(index => new RootSource(RootSourceKind.Local, 10 + index), index => index),
            []),
        LazyRootFrame = new([], []),
        RootFrameSaves = ImmutableDictionary<StructuredExceptionGroupId, int>.Empty.Add(Group, 77),
    };
}
