using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataTypeFinder : ITypeFinder
{
    private readonly ImmutableDictionary<string, ImmutableArray<TypeDefinitionModel>>
        _typesByFullName;
    private readonly IMetadataAvailabilityValidator _availability;

    public MetadataTypeFinder(
        ImmutableArray<TypeDefinitionModel> types,
        IMetadataAvailabilityValidator availability)
    {
        _typesByFullName = types
            .GroupBy(type => type.FullName, StringComparer.Ordinal)
            .ToImmutableDictionary(
                group => group.Key,
                group => group.ToImmutableArray(),
                StringComparer.Ordinal);
        _availability = availability;
    }

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
