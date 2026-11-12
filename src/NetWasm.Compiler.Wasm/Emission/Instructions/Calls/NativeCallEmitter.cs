using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Calls;

internal sealed class NativeCallEmitter(INativeAbiCallEmitter calls) : ICallEmitter
{
    private readonly INativeAbiCallEmitter _calls = calls ?? throw new ArgumentNullException(nameof(calls));

    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(functionIndices);
        var instruction = request.Instruction;
        if (instruction.Instruction.Operation != CilOperation.Call ||
            request.Consumed != request.Method.Signature.ParameterTypes.Length ||
            request.ArgumentBase < 0 ||
            request.ArgumentBase != instruction.Stack.Count - request.Consumed)
            throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                "A native call requires the exact static call argument tail.", request.Method.CanonicalName));
        if (!instruction.Target.NativeImports.ByMethod.TryGetValue(request.Method.Definition.Key, out var native) ||
            !native.Method.HasEquivalentDescriptorFacts(request.Method))
            throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                "A native call has no matching published ABI plan.", request.Method.CanonicalName,
                instruction.Instruction.Offset));
        _calls.Emit(new(instruction, native.Abi.Lowering, request.ArgumentBase, request.Consumed,
            native.Abi.Import.HasWasmImportLinkage, request.Method.CanonicalName)
        {
            Import = request.Method,
        }, code, functionIndices);
    }
}
