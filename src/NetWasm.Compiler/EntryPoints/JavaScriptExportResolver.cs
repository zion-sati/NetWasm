using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class JavaScriptExportResolver : ICompilationExportResolver
{
    public IReadOnlyList<CompilationExportCandidate> Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(options);
        return metadata.EntryAssemblyMethods
            .Where(method => method.JSExport is not null)
            .OrderBy(
                method => method.JSExport!.ExportName ?? method.Name,
                StringComparer.Ordinal)
            .Select(method =>
            {
                var name = method.JSExport!.ExportName ?? method.Name;
                return new CompilationExportCandidate(
                    new ProgramExport(name, method.Key),
                    DiagnosticCode.InvalidCommandLine,
                    $"duplicate Wasm export name '{name}'",
                    method);
            })
            .ToImmutableArray();
    }
}
