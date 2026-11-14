using System;
using System.Linq;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerDiagnosticOriginBinder : ICompilerDiagnosticOriginBinder
{
    public CompilerDiagnostic Bind(CompilerDiagnostic diagnostic,
        ImmutableArray<CompilationAssemblySource> assemblies,
        ImmutableArray<CompilationReferenceOrigin> origins)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.Origin is not null || diagnostic.Context is not { } context ||
            assemblies.IsDefaultOrEmpty || origins.IsDefaultOrEmpty)
            return diagnostic;
        var sources = assemblies.Where(source => source.Identity == context.Source.Assembly).Distinct().ToArray();
        if (sources.Length != 1)
            return diagnostic;
        var matches = origins.Where(origin => origin.Role == CompilationReferenceRole.Implementation &&
            StringComparer.Ordinal.Equals(origin.Path, sources[0].Path)).Distinct().ToArray();
        return matches.Length == 1 ? diagnostic with { Origin = matches[0] } : diagnostic;
    }
}
