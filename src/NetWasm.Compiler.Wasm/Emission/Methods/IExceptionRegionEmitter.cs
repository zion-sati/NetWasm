using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IExceptionRegionEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        StructuredMethod method,
        StructuredExceptionRegion region,
        MethodEmissionContext context,
        bool isOutermost,
        ModuleDataPlan moduleData,
        Action<StructuredSequence, MethodEmissionContext, int?, int?, int?> emitSequence,
        int? loopBreakDepth = null,
        int? loopContinueDepth = null,
        int? dispatcherContinueDepth = null);
}
