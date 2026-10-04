using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NullableBoxEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void AddressEmissionLeavesNullUnlessTheSourceHasAValue(
        WasmTarget target,
        bool aggregate)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = ThroughContract(CreateEmitter(layouts));
        var writer = new RecordingInstructionWriter();
        var underlyingType = aggregate
            ? CliTypeIdentity.Named(Assembly, "Test", "Value", true)
            : CliTypeIdentity.Primitive("i4", CliValueKind.I4);

        emitter.Emit(writer, underlyingType, sourceAddressLocal: 2, targetLocal: 3);

        var instructions = writer.ToInstructions();
        Assert.Equal(
            target == WasmTarget.Wasm64 ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
            instructions[0].Opcode);
        Assert.Equal(0, target == WasmTarget.Wasm64
            ? instructions[0].Operand.Signed64Value
            : instructions[0].Operand.SignedValue);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned(3)), instructions[1]);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(2)), instructions[2]);
        Assert.Equal(WasmOpcodes.I32Load8Unsigned, instructions[3].Opcode);
        Assert.Equal(0U, instructions[3].Operand.Offset);
        Assert.Equal(WasmOpcodes.If, instructions[4].Opcode);
        Assert.Equal(WasmOpcodes.End, instructions[^1].Opcode);
        Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.Else);

        var allocation = Assert.Single(instructions.Where(instruction =>
            instruction.Opcode == WasmOpcodes.Call &&
            instruction.Operand.UnsignedValue == (uint)WasmRuntimeImports.CreateCatalog()
                .Resolve(RuntimeImportSymbol.Allocate)));
        Assert.True(instructions.IndexOf(allocation) > 4);
        Assert.Equal(underlyingType, layouts.ObjectIdentityRequest);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Opcode is WasmOpcodes.LocalSet or WasmOpcodes.LocalTee &&
            instruction.Operand.UnsignedValue == 2);
        Assert.Equal(2, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.LocalGet &&
            instruction.Operand.UnsignedValue == 2));

        if (aggregate)
        {
            Assert.Contains(instructions, instruction =>
                instruction.Opcode == WasmOpcodes.Prefixed &&
                instruction.Operand.Alignment == WasmOpcodes.MemoryCopy);
            Assert.DoesNotContain(instructions, instruction =>
                instruction.Opcode == WasmOpcodes.I32Store);
        }
        else
        {
            Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load);
            Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Store);
            Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.Prefixed);
        }
    }

    [Fact]
    public void AddressEmissionRejectsAliasedLocalsBeforeWritingOrResolvingTheBox()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = ThroughContract(CreateEmitter(layouts));
        var writer = new RecordingInstructionWriter();

        var error = Assert.Throws<ArgumentException>(() => emitter.Emit(
            writer,
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            sourceAddressLocal: 2,
            targetLocal: 2));

        Assert.Equal("targetLocal", error.ParamName);
        Assert.Empty(writer.ToInstructions());
        Assert.Null(layouts.ObjectIdentityRequest);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void StackEmissionPreservesItsSourceBeforeClearingTheResult(WasmTarget target)
    {
        var request = Request();
        var originalStack = request.Stack.ToArray();
        var underlyingType = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = ThroughContract(CreateEmitter(layouts));
        var directWriter = new RecordingInstructionWriter();
        var targetLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals, 0, CliValueKind.ManagedReference, layouts.Target);

        emitter.Emit(directWriter, underlyingType, request.Context.ObjectTemporary, targetLocal);
        Assert.Equal(originalStack, request.Stack);
        emitter.Emit(request, GetCodeWriter(request), underlyingType);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request)).ToInstructions();
        var sourceLocal = WasmLocalLayoutPlanner.GetEvaluationStackLocal(
            request.Context.StackLocals, 0, CliValueKind.ValueType, layouts.Target);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)sourceLocal)), instructions[0]);
        Assert.Equal(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)request.Context.ObjectTemporary)), instructions[1]);
        Assert.Equal(directWriter.ToInstructions(), instructions.Skip(2));
        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
    }

    [Fact]
    public void EmitBoxesTheScalarPayloadOnlyWhenItHasAValue()
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Primitive("i4", CliValueKind.I4));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load8Unsigned, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.If, GetCodeBytes(request));
    }

    [Fact]
    public void EmitCopiesAValueTypePayloadIntoItsUnderlyingBox()
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Named(Assembly, "Test", "Value", true));

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.MemoryCopy, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, WasmOpcodes.I32Constant, 4L)]
    [InlineData(WasmTarget.Wasm64, WasmOpcodes.I64Constant, 8L)]
    public void EmitUsesTheTargetObjectHeaderForItsPayload(
        WasmTarget target,
        byte constantOpcode,
        long expectedOffset)
    {
        var request = Request();

        Emit(request, CliTypeIdentity.Primitive("i4", CliValueKind.I4), target);

        var instructions = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions();
        Assert.Contains(instructions, instruction =>
            instruction.Opcode == constantOpcode &&
            (target == WasmTarget.Wasm64
                ? instruction.Operand.Signed64Value
                : instruction.Operand.SignedValue) == expectedOffset);
    }

    private static void Emit(
        InstructionEmissionRequest request,
        CliTypeIdentity underlyingType,
        WasmTarget target = WasmTarget.Wasm32)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emitter = ThroughContract(CreateEmitter(layouts));
        emitter.Emit(request, GetCodeWriter(request), underlyingType);
    }

    private static INullableBoxEmitter ThroughContract(INullableBoxEmitter emitter) => emitter;

    private static NullableBoxEmitter CreateEmitter(RecordingLayoutProvider layouts) =>
        new NullableBoxEmitter(
            layouts,
            new AddressInstructionEmitter(layouts),
            layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7));

    private static InstructionEmissionRequest Request() =>
        CreateInstructionRequest(
            CilOperation.Box,
            [CliValueKind.ValueType],
            new CilOperand.TypeIdentity(CliTypeIdentity.GenericInstantiation(
                CliTypeIdentity.Named(Assembly, "System", "Nullable`1", true),
                [CliTypeIdentity.Primitive("i4", CliValueKind.I4)])),
            CreateMethodEmissionContext() with
            {
                ValueLayout = new ValueFrameLayout(
                    16,
                    [],
                    [],
                    ImmutableDictionary<int, int>.Empty.Add(0, 4),
                    []),
            });
}
