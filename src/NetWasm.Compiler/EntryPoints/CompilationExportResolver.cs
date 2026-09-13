using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class CompilationExportResolver : ICompilationExportsResolver
{
    private readonly ImmutableArray<ICompilationExportResolver> _contributors;
    private readonly ICompilationExportNameValidator _names;

    public CompilationExportResolver(
        IEnumerable<ICompilationExportResolver> contributors,
        ICompilationExportNameValidator names)
    {
        _contributors = (contributors ??
            throw new ArgumentNullException(nameof(contributors))).ToImmutableArray();
        _names = names ?? throw new ArgumentNullException(nameof(names));
        if (_contributors.IsEmpty)
            throw new InvalidOperationException(
                "At least one compilation export resolver is required.");
    }

    public ImmutableArray<ProgramExport> Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        ISymbolFormatter symbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(options);

        var candidates = new List<CompilationExportCandidate>();
        foreach (var contributor in _contributors)
            AddAndValidate(
                candidates,
                contributor.Resolve(metadata, methods, options),
                symbols);
        return candidates.Select(candidate => candidate.Export).ToImmutableArray();
    }

    private void AddAndValidate(
        List<CompilationExportCandidate> candidates,
        IReadOnlyList<CompilationExportCandidate> additions,
        ISymbolFormatter symbols)
    {
        candidates.AddRange(additions);
        _names.Validate(symbols, candidates);
    }
}
