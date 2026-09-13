using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataSymbolFormatterFactory(
    ITypeRepositoryFactory repositories) : ISymbolFormatterFactory
{
    private readonly ITypeRepositoryFactory _repositories =
        repositories ?? throw new ArgumentNullException(nameof(repositories));

    public ISymbolFormatter Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MetadataSymbolFormatter(_repositories.Create(snapshot));
    }
}
