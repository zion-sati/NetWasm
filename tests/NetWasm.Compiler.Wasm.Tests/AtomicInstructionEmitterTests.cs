using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Memory;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class AtomicInstructionEmitterTests
{
    [Fact]
    public void CompareExchangeConsumesValueAndComparandAndReturnsOldValue()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new AtomicInstructionEmitter(
            layouts,
            CreateTypeOperands(new FakeProgram()));
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [CliValueKind.ManagedAddress, CliValueKind.I4, CliValueKind.I4],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.I4)));

        emitter.Emit(request);

        Assert.Equal([CliValueKind.I4], request.Stack);
        Assert.Contains(WasmOpcodes.If, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
    }

    [Fact]
    public void ReferenceCompareExchangeUsesTargetWidthOnMemory64()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var emitter = new AtomicInstructionEmitter(
            layouts,
            CreateTypeOperands(new FakeProgram()));
        var reference = CliTypeIdentity.Named(
            new("Tests"),
            "Tests",
            "Reference",
            isValueType: false);
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [
                CliValueKind.ManagedAddress,
                CliValueKind.ManagedReference,
                CliValueKind.ManagedReference,
            ],
            new CilOperand.TypeIdentity(reference));

        emitter.Emit(request);

        Assert.Equal([CliValueKind.ManagedReference], request.Stack);
        Assert.Contains(WasmOpcodes.I64Load, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I64Store, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I64Equal, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedReference)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4)]
    public void CompareExchangeUsesI32ForNonWidenedTargetAndValueCombinations(
        WasmTarget target,
        CliValueKind kind)
    {
        var layout = target == WasmTarget.Wasm64
            ? WasmTargetLayout.Wasm64
            : WasmTargetLayout.Wasm32;
        var emitter = new AtomicInstructionEmitter(
            new RecordingLayoutProvider(layout),
            CreateTypeOperands(new FakeProgram()));
        var identity = kind == CliValueKind.ManagedReference
            ? CliTypeIdentity.Named(
                new("Tests"),
                "Tests",
                "Reference",
                isValueType: false)
            : CliTypeIdentity.FromStackKind(kind);
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [CliValueKind.ManagedAddress, kind, kind],
            new CilOperand.TypeIdentity(identity));

        emitter.Emit(request);

        Assert.Equal([kind], request.Stack);
        Assert.Contains(WasmOpcodes.I32Load, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Store, GetCodeBytes(request));
        Assert.Contains(WasmOpcodes.I32Equal, GetCodeBytes(request));
    }

    [Fact]
    public void CompareExchangeRejectsUnsupportedValueKinds()
    {
        var emitter = new AtomicInstructionEmitter(
            new RecordingLayoutProvider(),
            CreateTypeOperands(new FakeProgram()));
        var request = CreateInstructionRequest(
            CilOperation.CompareExchange,
            [CliValueKind.ManagedAddress, CliValueKind.F4, CliValueKind.F4],
            new CilOperand.TypeIdentity(
                CliTypeIdentity.FromStackKind(CliValueKind.F4)));

        var exception = Assert.Throws<CompilerException>(() => emitter.Emit(request));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
    }
}
