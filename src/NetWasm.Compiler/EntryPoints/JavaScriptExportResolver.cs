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
        // WIT components expose canonical exports. Rooting otherwise-unused
        // JavaScript exports retains helpers and host imports that the component
        // cannot expose. Raw JavaScript sessions may still select a WIT world
        // for their imports, so preserve their explicit boundary override.
        if (options.WitPath is not null && !options.UseJavaScriptExportBoundary)
        {
            return [];
        }
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
