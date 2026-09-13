using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class NativeIntegerConstructionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, CliValueKind.I8, "u8", WasmOpcodes.I32WrapI64)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4, "i4", WasmOpcodes.I64ExtendI32Signed)]
    [InlineData(WasmTarget.Wasm64, CliValueKind.I4, "u4", WasmOpcodes.I64ExtendI32Unsigned)]
    public void EmitPublishesConvertedNativeInteger(
        WasmTarget target,
        CliValueKind source,
        string parameterName,
        byte expectedOpcode)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var emission = CreateInstructionRequest(CilOperation.NewObject, [source]);
        var signature = new MethodSignatureModel(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            [CliTypeIdentity.Primitive(parameterName, source)]);
        var emitter = new NativeIntegerConstructionEmitter(
            layouts,
            new NativeIntegerConversionEmitter(layouts));

        ((INativeIntegerConstructionEmitter)emitter).Emit(
            new NativeIntegerConstructionRequest(emission, signature, 0),
            GetCodeWriter(emission));

        Assert.Equal([CliValueKind.NativeInt], emission.Stack);
        Assert.Contains(expectedOpcode, GetCodeBytes(emission));
    }

    [Fact]
    public void EmitRejectsInvalidRequestsAndMissingCollaborators()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new NativeIntegerConstructionEmitter(
            layouts,
            new NativeIntegerConversionEmitter(layouts));
        var emission = CreateInstructionRequest(CilOperation.NewObject);
        var emptySignature = MethodSignatureModel.Create(CliValueKind.Void);

        Assert.Throws<ArgumentNullException>(() =>
            ((INativeIntegerConstructionEmitter)emitter).Emit(
                null!,
                GetCodeWriter(emission)));
        Assert.Throws<InvalidOperationException>(() =>
            ((INativeIntegerConstructionEmitter)emitter).Emit(
                new NativeIntegerConstructionRequest(emission, emptySignature, 0),
                GetCodeWriter(emission)));
        Assert.Throws<ArgumentNullException>(() =>
            new NativeIntegerConstructionEmitter(
                null!,
                new NativeIntegerConversionEmitter(layouts)));
        Assert.Throws<ArgumentNullException>(() =>
            new NativeIntegerConstructionEmitter(layouts, null!));
    }
}
