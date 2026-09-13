using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class WitPostReturnExportResolver : ICompilationExportResolver
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
            .Where(method => method.WitPostReturn is not null)
            .OrderBy(
                method => method.WitPostReturn!.InterfaceName,
                StringComparer.Ordinal)
            .ThenBy(
                method => method.WitPostReturn!.FunctionName,
                StringComparer.Ordinal)
            .Select(method =>
            {
                var postReturn = method.WitPostReturn!;
                var name = CanonicalAbiNames.PostReturn(
                    postReturn.InterfaceName,
                    postReturn.FunctionName,
                    options.Target);
                return new CompilationExportCandidate(
                    new ProgramExport(name, method.Key),
                    DiagnosticCode.ComponentContract,
                    $"duplicate WIT post-return export '{name}'",
                    method);
            })
            .ToImmutableArray();
    }
}
