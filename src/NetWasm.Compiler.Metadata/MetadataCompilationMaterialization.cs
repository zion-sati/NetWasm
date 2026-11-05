using System.Collections.Frozen;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record MetadataCompilationMaterialization(
    ImmutableArray<ManagedAssembly> Assemblies,
    FrozenDictionary<EntityKey, TypeDefinitionModel> Types,
    FrozenDictionary<EntityKey, FieldDefinitionModel> Fields,
    FrozenDictionary<EntityKey, MethodDefinitionModel> Methods,
    ImmutableDictionary<string, MetadataAssemblySnapshot> MetadataAssemblies,
    ImmutableDictionary<string, string> ReferenceAssemblyAliases)
{
    public FrozenDictionary<EntityKey, PropertyDefinitionModel> Properties { get; init; } =
        FrozenDictionary<EntityKey, PropertyDefinitionModel>.Empty;

    // Types by assembly and full name, and by full name alone: built once per compile for
    // every resolver and finder.
    public FrozenDictionary<(string Assembly, string FullName), TypeDefinitionModel> TypesByName { get; init; } =
        FrozenDictionary<(string, string), TypeDefinitionModel>.Empty;
    public FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>> TypesByFullName { get; init; } =
        FrozenDictionary<string, ImmutableArray<TypeDefinitionModel>>.Empty;
}
