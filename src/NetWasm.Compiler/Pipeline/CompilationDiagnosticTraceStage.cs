using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Pipeline;

internal interface ICompilationDiagnosticTraceStage
{
    void Write(
        string path,
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        CompilationLayouts layouts,
        MethodDefinitionModel entryPoint,
        WasmTarget target,
        int staticDataEnd);
}

internal sealed class CompilationDiagnosticTraceStage(
    ICompilerDiagnosticTraceWriter traces,
    ITypeRepositoryFactory types,
    IFieldRepositoryFactory fields,
    ISymbolFormatterFactory symbols,
    ITypeDefinitionResolverFactory typeDefinitions,
    ITypeLayoutProviderFactory typeLayouts,
    IInstanceFieldLayoutProviderFactory instanceFields,
    IStaticFieldLayoutProviderFactory staticFields) : ICompilationDiagnosticTraceStage
{
    private readonly ICompilerDiagnosticTraceWriter _traces = traces ??
        throw new ArgumentNullException(nameof(traces));
    private readonly ITypeRepositoryFactory _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepositoryFactory _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ISymbolFormatterFactory _symbols = symbols ??
        throw new ArgumentNullException(nameof(symbols));
    private readonly ITypeDefinitionResolverFactory _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeLayoutProviderFactory _typeLayouts = typeLayouts ??
        throw new ArgumentNullException(nameof(typeLayouts));
    private readonly IInstanceFieldLayoutProviderFactory _instanceFields = instanceFields ??
        throw new ArgumentNullException(nameof(instanceFields));
    private readonly IStaticFieldLayoutProviderFactory _staticFields = staticFields ??
        throw new ArgumentNullException(nameof(staticFields));

    public void Write(
        string path,
        MetadataCompilationSnapshot metadata,
        ReachableProgram program,
        WasmMethodLoweringResult lowering,
        CompilationLayouts layouts,
        MethodDefinitionModel entryPoint,
        WasmTarget target,
        int staticDataEnd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(lowering);
        ArgumentNullException.ThrowIfNull(layouts);
        var symbols = _symbols.Create(metadata);
        var definitions = _typeDefinitions.Create(metadata);
        _traces.WriteTrace(
            path,
            symbols,
            _types.Create(metadata),
            _fields.Create(metadata),
            program,
            lowering,
            layouts.Snapshot,
            _typeLayouts.Create(definitions, layouts.Snapshot),
            _instanceFields.Create(layouts.Snapshot),
            _staticFields.Create(layouts.Snapshot),
            entryPoint,
            target,
            staticDataEnd);
    }
}
