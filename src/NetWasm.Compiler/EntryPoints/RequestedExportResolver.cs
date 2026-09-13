using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class RequestedExportResolver : ICompilationExportResolver
{
    public IReadOnlyList<CompilationExportCandidate> Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(options);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var exports = ImmutableArray.CreateBuilder<CompilationExportCandidate>(
            options.Exports.Length);
        foreach (var requested in options.Exports)
        {
            if (!names.Add(requested.Name))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.InvalidCommandLine,
                    $"duplicate requested export name '{requested.Name}'"));
            }

            var method = methods.FindMethod(
                metadata.EntryAssemblyIdentity,
                requested.TypeName,
                requested.MethodName);
            exports.Add(new(
                new ProgramExport(requested.Name, method.Key),
                DiagnosticCode.InvalidCommandLine,
                $"duplicate requested export name '{requested.Name}'",
                null));
        }
        return exports.ToImmutable();
    }
}
