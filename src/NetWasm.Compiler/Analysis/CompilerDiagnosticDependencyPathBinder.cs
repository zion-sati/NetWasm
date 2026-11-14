using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ICompilerDiagnosticDependencyPathBinder
{
    CompilerDiagnostic Bind(CompilerDiagnostic diagnostic,
        IReadOnlyDictionary<string, ImmutableStack<CompilerDependencyStep>> paths);
}

internal sealed class CompilerDiagnosticDependencyPathBinder : ICompilerDiagnosticDependencyPathBinder
{
    public CompilerDiagnostic Bind(CompilerDiagnostic diagnostic,
        IReadOnlyDictionary<string, ImmutableStack<CompilerDependencyStep>> paths)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(paths);
        var source = diagnostic.DependencyChain.IsDefaultOrEmpty
            ? diagnostic.Context : diagnostic.DependencyChain[0].Source;
        // A field signature identifies its value, not the containing instance.
        // Without the closed owner, a same-valued field cannot supply a caller.
        if (source is { DeclaringType: null } &&
            (source.Source.MetadataToken & unchecked((int)0xff000000)) == 0x04000000)
            return diagnostic;
        var identity = source?.DeclaringType?.CanonicalName ?? source?.InstanceIdentity;
        if (identity is null ||
            !paths.TryGetValue(identity, out var path) || path.IsEmpty)
            return diagnostic;
        return diagnostic with
        {
            DependencyChain = path.Reverse().Concat(diagnostic.DependencyChain.IsDefault
                ? [] : diagnostic.DependencyChain).ToImmutableArray(),
        };
    }
}
