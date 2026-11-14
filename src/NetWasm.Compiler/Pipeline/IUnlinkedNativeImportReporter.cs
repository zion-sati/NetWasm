using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface IUnlinkedNativeImportReporter
{
    void Report(ReachableProgram program, ISymbolFormatter symbols);
}
