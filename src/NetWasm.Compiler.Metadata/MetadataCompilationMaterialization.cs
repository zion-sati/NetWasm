using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record MetadataCompilationMaterialization(
    ImmutableArray<ManagedAssembly> Assemblies,
    ImmutableDictionary<EntityKey, TypeDefinitionModel> Types,
    ImmutableDictionary<EntityKey, FieldDefinitionModel> Fields,
    ImmutableDictionary<EntityKey, MethodDefinitionModel> Methods,
    ImmutableDictionary<string, MetadataAssemblySnapshot> MetadataAssemblies,
    ImmutableDictionary<string, string> ReferenceAssemblyAliases);
