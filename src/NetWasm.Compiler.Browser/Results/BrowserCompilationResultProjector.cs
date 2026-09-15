using System;

namespace NetWasm.Compiler.Browser.Results;

internal sealed class BrowserCompilationResultProjector : IBrowserCompilationResultProjector
{
    public BrowserCompilationResult Project(CompilationResult result, CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        return new BrowserCompilationResult(
            result.ApplicationModule.AsSpan().ToArray(),
            result.StaticDataEnd,
            result.RuntimeFeatures,
            result.FunctionImports,
            result.InteropManifest,
            new BrowserCompilationEntryPoint(
                options.EntryAssemblyPath,
                options.EntryTypeName,
                options.EntryMethodName,
                options.EntryMethodToken,
                options.EntryPointKind));
    }
}
