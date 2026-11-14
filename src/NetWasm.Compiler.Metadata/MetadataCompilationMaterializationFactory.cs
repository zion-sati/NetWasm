using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using NetWasm.Compiler.Core;

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
        var metadataAssemblies = snapshot.Assemblies.ToImmutableDictionary(
            assembly => assembly.Identity.Name,
            assembly => assembly.Metadata,
            StringComparer.Ordinal);
        // Built once, then only read by every worker: frozen maps look up without a tree walk.
        return new MetadataCompilationMaterialization(
            snapshot.Assemblies,
            Freeze(snapshot.Types, type => type.Key),
            Freeze(snapshot.Assemblies.SelectMany(assembly => assembly.Fields.Values), field => field.Key),
            Freeze(snapshot.Methods, method => method.Key),
            metadataAssemblies,
            snapshot.ReferenceAssemblyAliases ?? ImmutableDictionary<string, string>.Empty)
        {
            OriginalContractTypes = snapshot.OriginalContractAssemblies
                .SelectMany(assembly => assembly.Types.Values.Select(type =>
                    new OriginalContractType(assembly.Metadata, type)))
                .ToImmutableDictionary(contract => (contract.Source.Identity, contract.Definition.FullName)),
            Properties = Freeze(
                snapshot.Assemblies.SelectMany(assembly => assembly.Properties.Values),
                property => property.Key),
            TypesByName = MetadataTypeDefinitionResolver.Index(snapshot.Types),
            TypesByFullName = MetadataTypeFinder.Index(snapshot.Types, metadataAssemblies),
        };
    }

    // A frozen map that, like building an immutable one, refuses a duplicate key.
    private static FrozenDictionary<EntityKey, T> Freeze<T>(IEnumerable<T> values, Func<T, EntityKey> key)
    {
        var map = new Dictionary<EntityKey, T>();
        foreach (var value in values) map.Add(key(value), value);
        return map.ToFrozenDictionary();
    }
}
