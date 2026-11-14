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
        IMetadataAvailabilityValidator availability,
        IReadOnlyDictionary<string, MetadataAssemblySnapshot>? assemblies = null)
        : this(Index(types, assemblies), availability)
    {
    }

    public MetadataTypeFinder(
        FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>> typesByFullName,
        IMetadataAvailabilityValidator availability)
    {
        _typesByFullName = typesByFullName;
        _availability = availability;
    }

    // Types by full name, in their given order. With the assemblies, a type that an alias
    // places in another assembly is found there only, not also under the alias.
    internal static FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>> Index(
        IEnumerable<TypeDefinitionModel> types,
        IReadOnlyDictionary<string, MetadataAssemblySnapshot>? assemblies = null) => types
        .Where(type => assemblies is null ||
            !assemblies.TryGetValue(type.Key.Assembly.Name, out var source) ||
            source.AssemblyIdentityAliases.Canonicalize(type.Key.Assembly, type.FullName) == type.Key.Assembly)
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
