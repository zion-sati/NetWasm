using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticMethodTraceFormatter
{
    string FormatMethods(
        ISymbolFormatter symbols,
        ReachableProgram program,
        WasmMethodLoweringResult lowering);
}
