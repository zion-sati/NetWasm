using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class InteropIntrinsicEmitterTests
{
    [Fact]
    public void EmitsObjectDisposalThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new JSObjectDisposeIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.JSObjectDispose,
            [CliValueKind.ManagedReference]);
        var originalInstruction = request.Instruction;
        var code = EmitterTestSupport.GetCodeWriter(request.Instruction);
        request = WithInteropImports(request, releaseHandle: 41);

        EmitThroughCapability(emitter, request, code);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(originalInstruction));
    }

    [Fact]
    public void EmitsSubscriptionDisposalThroughItsCapability()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new JSSubscriptionDisposeIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.JSSubscriptionDispose,
            [CliValueKind.ManagedReference]);
        var originalInstruction = request.Instruction;
        var code = EmitterTestSupport.GetCodeWriter(request.Instruction);
        request = WithInteropImports(request, releaseSubscription: 41);

        EmitThroughCapability(emitter, request, code);

        Assert.NotEmpty(EmitterTestSupport.GetCodeBytes(originalInstruction));
    }

    [Fact]
    public void ObjectDisposalRequiresAReleaseHandleImport()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new JSObjectDisposeIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.JSObjectDispose,
            [CliValueKind.ManagedReference]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(
            emitter,
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction)));
    }

    [Fact]
    public void SubscriptionDisposalRequiresAReleaseSubscriptionImport()
    {
        var layouts = new RecordingLayoutProvider();
        var emitter = new JSSubscriptionDisposeIntrinsicEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            EmitterTestSupport.CreateAddressInstructions(layouts));
        var request = EmitterTestSupport.CreateRuntimeIntrinsicRequest(
            RuntimeIntrinsic.JSSubscriptionDispose,
            [CliValueKind.ManagedReference]);

        Assert.Throws<InvalidOperationException>(() => EmitThroughCapability(
            emitter,
            request,
            EmitterTestSupport.GetCodeWriter(request.Instruction)));
    }

    private static void EmitThroughCapability(
        IRuntimeIntrinsicEmitter emitter,
        RuntimeIntrinsicEmissionRequest request,
        IWasmInstructionWriter code) => emitter.Emit(
            request,
            code);

    private static RuntimeIntrinsicEmissionRequest WithInteropImports(
        RuntimeIntrinsicEmissionRequest request,
        int? releaseHandle = null,
        int? releaseSubscription = null)
    {
        var imports = request.Instruction.Target.InteropImports with
        {
            ReleaseHandle = releaseHandle is null
                ? default
                : OptionalFunctionIndex.At(releaseHandle.Value),
            ReleaseSubscription = releaseSubscription is null
                ? default
                : OptionalFunctionIndex.At(releaseSubscription.Value),
        };
        var target = request.Instruction.Target with { InteropImports = imports };
        var instruction = request.Instruction with { Target = target };
        var call = request.Call with { Instruction = instruction };
        return request with { Call = call };
    }
}
