using System;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.Browser.Results;

internal sealed class BrowserCompilationResultProjector : IBrowserCompilationResultProjector
{
    public BrowserCompilationResult Project(
        CompilationResult result, CompilerOptions options, ManagedExecutableEntryPointAbi? entryPointAbi)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        if ((options.EntryPointKind == CompilerEntryPointKind.ManagedExecutable) != (entryPointAbi is not null))
        {
            throw new ArgumentException(
                "A managed executable result requires its selected entry-point ABI; a raw function has none.",
                nameof(entryPointAbi));
        }

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
                options.EntryPointKind,
                entryPointAbi));
    }
}
