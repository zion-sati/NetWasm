using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal interface ICompilationExportNameValidator
{
    void Validate(
        ISymbolFormatter symbols,
        IEnumerable<CompilationExportCandidate> candidates);
}

internal sealed class CompilationExportNameValidator :
    ICompilationExportNameValidator
{
    public void Validate(
        ISymbolFormatter symbols,
        IEnumerable<CompilationExportCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(candidates);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!names.Add(candidate.Export.Name))
            {
                throw new CompilerException(new CompilerDiagnostic(
                    candidate.DuplicateDiagnosticCode,
                    candidate.DuplicateMessage,
                    candidate.DuplicateMethod is null
                        ? null
                        : symbols.Format(candidate.DuplicateMethod)));
            }
        }
    }
}
