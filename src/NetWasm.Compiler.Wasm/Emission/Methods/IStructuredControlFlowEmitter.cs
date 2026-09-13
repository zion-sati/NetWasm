using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IStructuredControlFlowEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredSequence sequence,
        MethodEmissionContext context,
        Func<StructuredBlockOccurrence, bool, bool, ConditionValue> emitBlock,
        Action<StructuredExceptionRegion, MethodEmissionContext, bool, int?, int?, int?>
            emitExceptionRegion,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? exceptionLeaveDepth = null,
        int? dispatcherContinueDepth = null);
}
