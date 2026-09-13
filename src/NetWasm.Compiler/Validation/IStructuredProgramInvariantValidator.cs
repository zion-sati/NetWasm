using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Validation;

internal interface IStructuredProgramInvariantValidator
{
    void Validate(ISymbolFormatter symbols, WasmMethodLoweringResult program);
}
