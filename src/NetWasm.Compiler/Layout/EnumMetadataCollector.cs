using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal sealed class EnumMetadataCollector(
    ITypeRepository typeDefinitions,
    ManagedTypeLayouts types,
    ManagedStaticDataBuildState state) : IEnumMetadataCollector
{
    private readonly ITypeRepository _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly ManagedTypeLayouts _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly ManagedStaticDataBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public void Collect()
    {
        foreach (var (type, layout) in _types.Objects.OrderBy(pair => pair.Value.TypeId))
        {
            var definition = _typeDefinitions.GetTypeDefinition(type);
            if (!definition.IsEnum)
                continue;

            _state.PendingEnumMetadata.Add(type, new PendingEnumMetadata(
                type,
                layout.TypeId,
                definition.EnumUnderlyingType,
                definition.IsFlagsEnum,
                definition.EnumMembers));
        }
    }
}
