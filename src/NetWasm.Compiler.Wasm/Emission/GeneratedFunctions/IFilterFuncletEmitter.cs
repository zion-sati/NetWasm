using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterFuncletEmitter
{
    FilterFuncletEmission Emit(
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        IFilterSequenceEmitter emitSequence,
        MemberExecutionPlan? memberExecution = null);
}
