using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StringIntrinsicEmitterTests
{
    [Fact]
    public void EmitsStringLengthThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new StringLengthIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringLength,
            [CliValueKind.ManagedReference]);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void EmitsCharacterAccessThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new StringCharacterAtIntrinsicEmitter(
            layouts,
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringCharacterAt,
            [CliValueKind.ManagedReference, CliValueKind.I4]);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    [Fact]
    public void EmitsUncheckedCharacterMutationThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new StringSetCharacterUncheckedIntrinsicEmitter(
            layouts,
            layouts);
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.StringSetCharacterUnchecked,
            [CliValueKind.ManagedReference, CliValueKind.I4, CliValueKind.I4]);

        EmitThroughCapability(emitter, request);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(request.Instruction));
    }

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request) => emitter.Emit(
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction));
}
