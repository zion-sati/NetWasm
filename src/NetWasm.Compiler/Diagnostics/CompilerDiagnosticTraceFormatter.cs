using System;
using System.Globalization;
using System.Text;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticTraceFormatter(
    ICompilerDiagnosticLayoutTraceFormatter layouts,
    ICompilerDiagnosticDispatchTraceFormatter dispatch,
    ICompilerDiagnosticMethodTraceFormatter methods) : ICompilerDiagnosticTraceFormatter
{
    private readonly ICompilerDiagnosticLayoutTraceFormatter _layouts =
        layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly ICompilerDiagnosticDispatchTraceFormatter _dispatch =
        dispatch ?? throw new ArgumentNullException(nameof(dispatch));
    private readonly ICompilerDiagnosticMethodTraceFormatter _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));

    public string FormatTrace(
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
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(typeLayouts);
        ArgumentNullException.ThrowIfNull(instanceFields);
        ArgumentNullException.ThrowIfNull(staticFields);
        ArgumentNullException.ThrowIfNull(entryPoint);
        var text = new StringBuilder();
        text.AppendLine("NETWASM COMPILER DIAGNOSTIC TRACE v1");
        text.Append("target=").AppendLine(target.ToString());
        text.Append("entry=").AppendLine(symbols.Format(entryPoint));
        text.Append("static-data-end=").AppendLine(
            staticDataEnd.ToString(CultureInfo.InvariantCulture));
        text.AppendLine();
        text.Append(_layouts.FormatLayouts(
            program,
            layouts,
            typeLayouts,
            instanceFields,
            staticFields,
            symbols,
            types,
            fields));
        text.Append(_dispatch.FormatDispatch(program));
        text.Append(_methods.FormatMethods(symbols, program, lowering));
        return text.ToString().ReplaceLineEndings("\n");
    }
}
