using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

// Adapter: a JSImport call site's evaluation stack is the invocation's argument source.
internal sealed class JSImportCallEmitter(IJSImportInvocationEmitter invocations) : ICallEmitter
{
    public void Emit(CallEmissionRequest request, IWasmInstructionWriter code, IFunctionIndexResolver functionIndices)
    {
        var instruction = request.Instruction;
        invocations.Emit(
            new JSImportInvocation(
                request.Method,
                instruction.Context,
                instruction.Stack,
                request.ArgumentBase,
                request.Consumed,
                instruction.Target,
                instruction.Instruction.Offset),
            code,
            functionIndices);
    }
}
