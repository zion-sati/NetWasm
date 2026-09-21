using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class AssignableTypeMetadataBuilder(
    ITypeIdentityResolver identities,
    ITypeDefinitionResolver typeDefinitions,
    ITypeRelationshipClassifier relationships,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IAssignableTypeMetadataBuilder
{
    private const int TypeIdSize = sizeof(int);

    private readonly (CliTypeIdentity Type, int TypeId)[] _runtimeInterfaceTypes =
        types.Objects
            .Select(pair =>
                (Type: identities.GetTypeIdentity(pair.Key), pair.Value.TypeId))
            .Concat(types.ConstructedObjects.Select(pair =>
                (Type: pair.Key, pair.Value.TypeId)))
            .Where(target => target.Type.Shape is not
                (CliTypeShape.SzArray or CliTypeShape.Array))
            .Where(target => target.Type.Shape is
                (CliTypeShape.Named or CliTypeShape.GenericInstantiation))
            .Where(target => IsClosedRuntimeInterface(typeDefinitions, target.Type))
            .ToArray();

    public AssignableTypeMetadataLayout Build(CliTypeIdentity candidate)
    {
        if (candidate.ContainsGenericParameters ||
            candidate.Shape == CliTypeShape.Named &&
            typeDefinitions.ResolveTypeIdentity(candidate).GenericArity != 0)
        {
            return default;
        }

        var typeIds = _runtimeInterfaceTypes
            .Where(target => relationships
                .Classify(candidate, target.Type)
                .IsAssignmentCompatible)
            .Select(target => target.TypeId)
            .Distinct()
            .Order()
            .ToArray();
        if (typeIds.Length == 0)
        {
            return default;
        }

        state.Cursor = ManagedTypeLayoutCompiler.Align(state.Cursor, TypeIdSize);
        var address = state.Cursor;
        var data = new byte[typeIds.Length * TypeIdSize];
        for (var index = 0; index < typeIds.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                data.AsSpan(index * TypeIdSize, TypeIdSize),
                typeIds[index]);
        }
        state.Segments.Add(new DataSegment(address, [.. data]));
        state.Cursor += data.Length;
        return new AssignableTypeMetadataLayout(address, typeIds.Length);
    }

    private static bool IsClosedRuntimeInterface(
        ITypeDefinitionResolver typeDefinitions,
        CliTypeIdentity type)
    {
        var definition = typeDefinitions.ResolveTypeIdentity(type);
        return definition.IsInterface &&
               (type.Shape == CliTypeShape.GenericInstantiation ||
                definition.GenericArity == 0);
    }
}
