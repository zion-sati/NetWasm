using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class MemberMaterializationEmitterTests
{
    [Theory]
    [InlineData(CilOperation.MaterializeMethod, WasmTarget.Wasm32)]
    [InlineData(CilOperation.MaterializeMethod, WasmTarget.Wasm64)]
    [InlineData(CilOperation.MaterializeField, WasmTarget.Wasm32)]
    [InlineData(CilOperation.MaterializeField, WasmTarget.Wasm64)]
    public void OneArgumentHandleRejectsGenericDeclaringTypesAndReturnsTheDescriptor(
        CilOperation operation,
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var exceptions = new RecordingExceptionEmitter();
        var request = CreateInstructionRequest(
            operation,
            [CliValueKind.NativeInt],
            new CilOperand.Index(1));

        CreateEmitter(layouts, exceptions).Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(
            [ManagedExceptionKind.Argument, ManagedExceptionKind.Argument],
            exceptions.Kinds);
        Assert.Equal(
            [
                WasmOpcodes.LocalGet,
                target == WasmTarget.Wasm64
                    ? WasmOpcodes.I64EqualZero
                    : WasmOpcodes.I32EqualZero,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.I32Load,
                WasmOpcodes.If,
                WasmOpcodes.End,
                WasmOpcodes.LocalGet,
                WasmOpcodes.LocalSet,
            ],
            ((RecordingInstructionWriter)GetCodeWriter(request))
                .ToInstructions()
                .Select(instruction => instruction.Opcode));
    }

    [Theory]
    [InlineData(CilOperation.MaterializeMethod, WasmTarget.Wasm32)]
    [InlineData(CilOperation.MaterializeMethod, WasmTarget.Wasm64)]
    [InlineData(CilOperation.MaterializeField, WasmTarget.Wasm32)]
    [InlineData(CilOperation.MaterializeField, WasmTarget.Wasm64)]
    public void TwoArgumentHandleRequiresTheExactNonzeroDeclaringType(
        CilOperation operation,
        WasmTarget target)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var exceptions = new RecordingExceptionEmitter();
        var request = CreateInstructionRequest(
            operation,
            [CliValueKind.NativeInt, CliValueKind.I4],
            new CilOperand.Index(2));

        CreateEmitter(layouts, exceptions).Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Equal(
            [ManagedExceptionKind.Argument, ManagedExceptionKind.Argument, ManagedExceptionKind.Argument],
            exceptions.Kinds);
        var load = Assert.Single(
            ((RecordingInstructionWriter)GetCodeWriter(request))
                .ToInstructions()
                .Where(instruction => instruction.Opcode == WasmOpcodes.I32Load));
        Assert.Equal((uint)layouts.DeclaringTypeIdOffset, load.Operand.Offset);
        var opcodes = ((RecordingInstructionWriter)GetCodeWriter(request))
            .ToInstructions()
            .Select(instruction => instruction.Opcode)
            .ToArray();
        Assert.Contains(WasmOpcodes.I32Equal, opcodes);
        Assert.DoesNotContain(WasmOpcodes.Call, opcodes);
    }

    [Fact]
    public void InvalidArityFailsAtTheOwningEmitter()
    {
        var request = CreateInstructionRequest(
            CilOperation.MaterializeField,
            [CliValueKind.NativeInt],
            new CilOperand.Index(3));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(
                new RecordingLayoutProvider(),
                new RecordingExceptionEmitter()).Emit(request));

        Assert.Contains("arity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingArityFailsAtTheOwningEmitter()
    {
        var request = CreateInstructionRequest(
            CilOperation.MaterializeMethod,
            [CliValueKind.NativeInt]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateEmitter(
                new RecordingLayoutProvider(),
                new RecordingExceptionEmitter()).Emit(request));

        Assert.Contains("arity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MemberMaterializationEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IImplicitExceptionEmitter exceptions) => new(
            layouts,
            layouts,
            new AddressInstructionEmitter(layouts),
            exceptions);

    private sealed class RecordingExceptionEmitter : IImplicitExceptionEmitter
    {
        public List<ManagedExceptionKind> Kinds { get; } = [];

        public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind) =>
            Kinds.Add(kind);
    }
}
