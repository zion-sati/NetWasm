using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class EnumMetadataCollector(
    ITypeDefinitionResolver typeDefinitions,
    ITypeIdentityResolver identities,
    ManagedTypeLayouts types,
    ImmutableHashSet<EnumMetadataRequirement> requirements,
    ManagedStaticDataBuildState state) : IEnumMetadataCollector
{
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly ImmutableHashSet<EnumMetadataRequirement> _requirements = requirements ??
        throw new ArgumentNullException(nameof(requirements));

    public void Collect()
    {
        var candidates = _types.Objects.Select(pair =>
                (Type: _identities.GetTypeIdentity(pair.Key), Layout: pair.Value))
            .Concat(_types.ConstructedObjects
                .Where(pair => pair.Key.Shape == CliTypeShape.GenericInstantiation)
                .Select(pair => (Type: pair.Key, Layout: pair.Value)))
            .OrderBy(pair => pair.Layout.TypeId);
        foreach (var (type, layout) in candidates)
        {
            var definition = _typeDefinitions.ResolveTypeIdentity(type);
            if (!definition.IsEnum)
                continue;

            var payload = EnumMetadataPayload.None;
            foreach (var requirement in _requirements)
            {
                if (requirement.Type is null || requirement.Type.Value == definition.Key)
                {
                    payload |= requirement.Payload;
                }
            }
            _state.PendingEnumMetadata.Add(type, new PendingEnumMetadata(
                definition.Key,
                type,
                layout.TypeId,
                definition.EnumUnderlyingType,
                definition.IsFlagsEnum,
                type.ContainsGenericParameters ||
                    (type.Shape == CliTypeShape.Named && definition.GenericArity > 0),
                payload == EnumMetadataPayload.None ? [] : definition.EnumMembers,
                payload));
        }
    }
}
