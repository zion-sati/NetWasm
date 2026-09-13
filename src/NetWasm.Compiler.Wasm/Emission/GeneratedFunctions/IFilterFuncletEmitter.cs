using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IFilterFuncletEmitter
{
    FilterFuncletEmission Emit(
        FilterFunclet filter,
        FilterEnvironmentLayout environment,
        IFilterSequenceEmitter emitSequence);
}
