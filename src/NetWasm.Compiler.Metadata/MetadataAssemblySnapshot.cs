using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed record MetadataAssemblySnapshot(
    AssemblyIdentity Identity,
    MetadataReader Reader,
    IReadOnlyDictionary<int, TypeDefinitionModel> Types,
    IReadOnlyDictionary<int, FieldDefinitionModel> Fields,
    IReadOnlyDictionary<int, MethodDefinitionModel> Methods,
    IReadOnlyDictionary<int, EntityHandle> BaseTypes,
    IReadOnlyDictionary<int, ImmutableArray<EntityHandle>> ImplementedInterfaces)
{
    internal AssemblyIdentityAliases AssemblyIdentityAliases { get; init; } =
        AssemblyIdentityAliases.Empty;

    internal MetadataAssemblySnapshot(
        AssemblyIdentity identity,
        MetadataReader reader,
        IReadOnlyDictionary<int, TypeDefinitionModel> types,
        IReadOnlyDictionary<int, FieldDefinitionModel> fields,
        IReadOnlyDictionary<int, MethodDefinitionModel> methods,
        IReadOnlyDictionary<int, EntityHandle> baseTypes)
        : this(
            identity,
            reader,
            types,
            fields,
            methods,
            baseTypes,
            ImmutableDictionary<int, ImmutableArray<EntityHandle>>.Empty)
    {
    }
}
