using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IManagedMethodSequenceEmitter
{
    ManagedMethodSequenceEmission Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context,
        InstructionModuleTarget target,
        IFunctionIndexResolver functionIndices,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? exceptionLeaveDepth = null);
}
