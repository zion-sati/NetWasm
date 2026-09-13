using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationMaterializationFactory :
    IMetadataCompilationMaterializationFactory
{
    private readonly ConditionalWeakTable<MetadataCompilationSnapshot,
        MetadataCompilationMaterialization> _materializations = new();

    public MetadataCompilationMaterialization Create(MetadataCompilationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _materializations.GetValue(snapshot, static current => Materialize(current));
    }

    private static MetadataCompilationMaterialization Materialize(
        MetadataCompilationSnapshot snapshot)
    {
        return new MetadataCompilationMaterialization(
            snapshot.Assemblies,
            snapshot.Types.ToImmutableDictionary(type => type.Key),
            snapshot.Assemblies
                .SelectMany(assembly => assembly.Fields.Values)
                .ToImmutableDictionary(field => field.Key),
            snapshot.Methods.ToImmutableDictionary(method => method.Key),
            snapshot.Assemblies.ToImmutableDictionary(
                assembly => assembly.Identity.Name,
                assembly => assembly.Metadata,
                StringComparer.Ordinal),
            snapshot.ReferenceAssemblyAliases ?? ImmutableDictionary<string, string>.Empty);
    }
}
