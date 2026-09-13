using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticDispatchTraceFormatter
{
    string FormatDispatch(ReachableProgram program);
}
