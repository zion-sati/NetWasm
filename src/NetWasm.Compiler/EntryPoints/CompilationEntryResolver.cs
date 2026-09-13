using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.EntryPoints;

internal sealed class CompilationEntryResolver(
    IEntryPointSelector entryPoints,
    ICompilationExportsResolver exports) : ICompilationEntryResolver
{
    private readonly IEntryPointSelector _entryPoints = entryPoints ??
        throw new ArgumentNullException(nameof(entryPoints));
    private readonly ICompilationExportsResolver _exports = exports ??
        throw new ArgumentNullException(nameof(exports));

    public CompilationEntry Resolve(
        MetadataCompilationSnapshot metadata,
        IMethodFinder methods,
        ISymbolFormatter symbols,
        CompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(options);
        return new(
            _entryPoints.Select(metadata, methods, symbols, options),
            _exports.Resolve(metadata, methods, symbols, options));
    }
}
