using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeFinder : ITypeFinder
{
    private readonly FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>>
        _typesByFullName;
    private readonly IMetadataAvailabilityValidator _availability;

    public MetadataTypeFinder(
        ImmutableArray<TypeDefinitionModel> types,
        IMetadataAvailabilityValidator availability)
        : this(Index(types), availability)
    {
    }

    public MetadataTypeFinder(
        FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>> typesByFullName,
        IMetadataAvailabilityValidator availability)
    {
        _typesByFullName = typesByFullName;
        _availability = availability;
    }

    // Types by full name, in their given order.
    internal static FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>> Index(
        IEnumerable<TypeDefinitionModel> types) => types
        .GroupBy(type => type.FullName, StringComparer.Ordinal)
        .ToFrozenDictionary(
            group => group.Key,
            group => group.ToImmutableArray(),
            StringComparer.Ordinal);

    public TypeDefinitionModel FindType(string fullName)
    {
        _availability.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        if (_typesByFullName.TryGetValue(fullName, out var matches) &&
            matches.Length == 1)
        {
            return matches[0];
        }

        throw new CompilerException(new CompilerDiagnostic(
            DiagnosticCode.AssemblyResolution,
            $"type '{fullName}' was not found uniquely"));
    }
}
