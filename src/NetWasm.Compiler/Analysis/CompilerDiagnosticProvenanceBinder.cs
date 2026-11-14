using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

internal interface ICompilerDiagnosticProvenanceBinder
{
    CompilerException Bind(CompilerException failure, CompilerDiagnosticContext source,
        ImmutableStack<CompilerDependencyStep> path);
}

internal sealed class CompilerDiagnosticProvenanceBinder : ICompilerDiagnosticProvenanceBinder
{
    public CompilerException Bind(CompilerException failure, CompilerDiagnosticContext source,
        ImmutableStack<CompilerDependencyStep> path)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(path);
        var diagnostic = failure.Diagnostic;
        if (diagnostic.Context is not null && (!diagnostic.DependencyChain.IsDefaultOrEmpty || path.IsEmpty))
            return failure;
        return new CompilerException(diagnostic with
        {
            Context = diagnostic.Context ?? source,
            DependencyChain = diagnostic.DependencyChain.IsDefaultOrEmpty
                ? path.Reverse().ToImmutableArray()
                : diagnostic.DependencyChain,
        }, failure);
    }
}
