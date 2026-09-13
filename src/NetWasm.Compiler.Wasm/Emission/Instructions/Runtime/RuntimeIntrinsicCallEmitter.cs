using NetWasm.Compiler.Wasm.Encoding;
using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class RuntimeIntrinsicCallEmitter(
    IRuntimeIntrinsicRegistry intrinsics,
    ITargetLayout layouts,
    IRuntimeIntrinsicEmitterRegistry emitters) : ICallEmitter
{
    public void Emit(
        CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = functionIndices;
        if (!intrinsics.TryGetIntrinsic(request.Method.Definition.Key, out var intrinsic))
        {
            throw new InvalidOperationException(
                $"Method '{request.Method.Definition.Key}' is not a runtime intrinsic.");
        }

        var emissionRequest = new RuntimeIntrinsicEmissionRequest(
            request,
            intrinsic,
            request.ConstrainedType,
            layouts.Target,
            functionIndices);
        emitters.Get(intrinsic).Emit(emissionRequest, code);
        var instruction = request.Instruction;
        instruction.Stack.RemoveRange(request.ArgumentBase, request.Consumed);
        if (request.Method.Signature.ReturnType != CliValueKind.Void)
        {
            instruction.Stack.Add(request.Method.Signature.ReturnType);
        }
    }
}
