using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class WitExportResolver : ICompilationExportResolver
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
            .Where(method => method.WitExport is not null)
            .OrderBy(
                method => method.WitExport!.InterfaceName,
                StringComparer.Ordinal)
            .ThenBy(
                method => method.WitExport!.FunctionName,
                StringComparer.Ordinal)
            .Select(method =>
            {
                var export = method.WitExport!;
                var name = CanonicalAbiNames.Export(
                    export.InterfaceName,
                    export.FunctionName,
                    options.Target);
                return new CompilationExportCandidate(
                    new ProgramExport(name, method.Key),
                    DiagnosticCode.ComponentContract,
                    $"duplicate WIT export '{name}'",
                    method);
            })
            .ToImmutableArray();
    }
}
