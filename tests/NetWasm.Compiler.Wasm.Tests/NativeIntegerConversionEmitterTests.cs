using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NativeIntegerConversionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4, false, WasmOpcodes.I32TruncateSaturateF32Signed)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F4, true, WasmOpcodes.I32TruncateSaturateF32Unsigned)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8, false, WasmOpcodes.I32TruncateSaturateF64Signed)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.F8, true, WasmOpcodes.I32TruncateSaturateF64Unsigned)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4, false, WasmOpcodes.I64TruncateSaturateF32Signed)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F4, true, WasmOpcodes.I64TruncateSaturateF32Unsigned)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8, false, WasmOpcodes.I64TruncateSaturateF64Signed)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.F8, true, WasmOpcodes.I64TruncateSaturateF64Unsigned)]
    public void EmitTruncatesFloatingSourcesWithoutTrappingAtNativeWidth(
        WasmTarget target, CliValueKind source, bool isUnsigned, byte expectedOpcode)
    {
        var request = CreateInstructionRequest(CilOperation.Nop);
        var emitter = new NativeIntegerConversionEmitter(
            new RecordingLayoutProvider(WasmTargetLayout.For(target)));

        ((INativeIntegerConversionEmitter)emitter).Emit(GetCodeWriter(request), source, isUnsigned);

        Assert.Equal(new byte[] { WasmOpcodes.Prefixed, expectedOpcode }, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmitExtendsInt32ForMemory64(bool isUnsigned)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var request = CreateInstructionRequest(CilOperation.Nop);
        var emitter = new NativeIntegerConversionEmitter(layouts);

        ((INativeIntegerConversionEmitter)emitter).Emit(
            GetCodeWriter(request),
            CliValueKind.I4,
            isUnsigned);

        Assert.Contains(
            isUnsigned
                ? WasmOpcodes.I64ExtendI32Unsigned
                : WasmOpcodes.I64ExtendI32Signed,
            GetCodeBytes(request));
    }

    [Fact]
    public void EmitWrapsInt64ForMemory32()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm32);
        var request = CreateInstructionRequest(CilOperation.Nop);
        var emitter = new NativeIntegerConversionEmitter(layouts);

        ((INativeIntegerConversionEmitter)emitter).Emit(
            GetCodeWriter(request),
            CliValueKind.I8,
            unsigned: false);

        Assert.Contains(WasmOpcodes.I32WrapI64, GetCodeBytes(request));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I4)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I8)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.NativeInt)]
    [InlineData(WasmTarget.Wasm32, CliValueKind.ManagedAddress)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.ManagedAddress)]
    public void EmitLeavesNativeWidthValuesUnchanged(
        WasmTarget target,
        CliValueKind source)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var request = CreateInstructionRequest(CilOperation.Nop);
        var emitter = new NativeIntegerConversionEmitter(layouts);

        ((INativeIntegerConversionEmitter)emitter).Emit(
            GetCodeWriter(request),
            source,
            unsigned: false);

        Assert.Empty(GetCodeBytes(request));
    }

    [Fact]
    public void EmitRejectsUnsupportedSourcesAndMissingLayout()
    {
        var layouts = new RecordingLayoutProvider();
        var request = CreateInstructionRequest(CilOperation.Nop);
        var emitter = new NativeIntegerConversionEmitter(layouts);

        Assert.Throws<InvalidOperationException>(() =>
            ((INativeIntegerConversionEmitter)emitter).Emit(
                GetCodeWriter(request),
                CliValueKind.ValueType,
                unsigned: false));
        Assert.Empty(GetCodeBytes(request));
        Assert.Throws<ArgumentNullException>(() =>
            new NativeIntegerConversionEmitter(null!));
    }
}
