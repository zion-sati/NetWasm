using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal interface ICompilerDiagnosticTraceWriter
{
    void WriteTrace(
        string path,
        ISymbolFormatter symbols,
        ITypeRepository types,
        IFieldRepository fields,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        ManagedLayoutSnapshot layouts,
        ITypeLayoutProvider typeLayouts,
        IInstanceFieldLayoutProvider instanceFields,
        IStaticFieldLayoutProvider staticFields,
        MethodDefinitionModel entryPoint,
        WasmTarget target,
        int staticDataEnd);
}
