using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Objects;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class UnsafeUnboxIntrinsicEmitter(
    IUnboxEmitter unboxes) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        if (request.Method.MethodArguments.Length != 1)
        {
            throw new InvalidOperationException("Unsafe.Unbox requires one closed type argument");
        }

        unboxes.Unbox(
            request.Instruction,
            code,
            request.Method.MethodArguments[0],
            copyValue: false);
    }
}
