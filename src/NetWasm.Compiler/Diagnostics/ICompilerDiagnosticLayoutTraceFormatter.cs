using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticLayoutTraceFormatter
{
    string FormatLayouts(
        ReachableProgram program,
        ManagedLayoutSnapshot layouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        ISymbolFormatter symbols,
        ITypeRepository types,
        IFieldRepository fields);
}
