using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterSequenceEmitter
{
    ManagedMethodSequenceEmission Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context);
}
