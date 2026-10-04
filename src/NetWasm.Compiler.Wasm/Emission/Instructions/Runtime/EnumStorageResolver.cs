using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class EnumStorageResolver(
    IEnumMetadataSource metadata,
    ITargetLayout layouts,
    IValueLayoutProvider values) : IEnumStorageResolver
{
    private readonly IEnumMetadataSource _metadata = metadata ??
        throw new ArgumentNullException(nameof(metadata));
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values = values ??
        throw new ArgumentNullException(nameof(values));

    public ImmutableArray<EnumStorage> Resolve() =>
        [.. _metadata.EnumMetadata
            .Where(entry => !entry.IsOpenDefinition)
            .Select(CreateStorage)];

    private EnumStorage CreateStorage(EnumMetadataLayout entry)
    {
        var layout = _values.GetValueLayout(entry.UnderlyingType);
        return new EnumStorage(
            entry.TypeId,
            entry.EnumType,
            entry.UnderlyingType,
            layout,
            WasmTargetLayout.Align(_layouts.Target.ObjectHeaderSize, layout.Alignment));
    }
}
