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
        var declared = new Dictionary<string, CompilationExportCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!declared.TryAdd(candidate.Export.Name, candidate))
            {
                var first = declared[candidate.Export.Name];
                throw new CompilerException(new CompilerDiagnostic(
                    candidate.DuplicateDiagnosticCode,
                    first.DuplicateMethod is null || candidate.DuplicateMethod is null
                        ? candidate.DuplicateMessage
                        : $"{candidate.DuplicateMessage}: declared by {Describe(symbols, first.DuplicateMethod)} " +
                          $"and {Describe(symbols, candidate.DuplicateMethod)}",
                    candidate.DuplicateMethod is null
                        ? null
                        : symbols.Format(candidate.DuplicateMethod)));
            }
        }
    }

    // Exports can come from any assembly, so name the assembly too.
    private static string Describe(ISymbolFormatter symbols, MethodDefinitionModel method) =>
        $"{symbols.Format(method)} in {method.Key.Assembly.Name}";
}
