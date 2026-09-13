using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticTraceWriter(
    ICompilerDiagnosticTraceFormatter formatter,
    ICompilerDiagnosticTextArtifactWriter textWriter) : ICompilerDiagnosticTraceWriter
{
    private readonly ICompilerDiagnosticTraceFormatter _formatter =
        formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly ICompilerDiagnosticTextArtifactWriter _textWriter =
        textWriter ?? throw new ArgumentNullException(nameof(textWriter));

    public void WriteTrace(
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
        int staticDataEnd)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _textWriter.WriteText(
            path,
            _formatter.FormatTrace(
                symbols,
                types,
                fields,
                program,
                lowering,
                layouts,
                typeLayouts,
                instanceFields,
                staticFields,
                entryPoint,
                target,
            staticDataEnd));
    }
}
