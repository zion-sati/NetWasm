using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class EnumMetadataCollector(
    ITypeRepository typeDefinitions,
    ManagedTypeLayouts types,
    ImmutableHashSet<EnumMetadataRequirement> requirements,
    ManagedStaticDataBuildState state) : IEnumMetadataCollector
{
    private readonly ITypeRepository _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));
    private readonly ImmutableHashSet<EnumMetadataRequirement> _requirements = requirements ??
        throw new ArgumentNullException(nameof(requirements));

    public void Collect()
    {
        foreach (var (type, layout) in _types.Objects.OrderBy(pair => pair.Value.TypeId))
        {
            var definition = _typeDefinitions.GetTypeDefinition(type);
            if (!definition.IsEnum)
                continue;

            var payload = EnumMetadataPayload.None;
            foreach (var requirement in _requirements)
            {
                if (requirement.Type is null || requirement.Type.Value == type)
                {
                    payload |= requirement.Payload;
                }
            }
            _state.PendingEnumMetadata.Add(type, new PendingEnumMetadata(
                type,
                layout.TypeId,
                definition.EnumUnderlyingType,
                definition.IsFlagsEnum,
                payload == EnumMetadataPayload.None ? [] : definition.EnumMembers,
                payload));
        }
    }
}
