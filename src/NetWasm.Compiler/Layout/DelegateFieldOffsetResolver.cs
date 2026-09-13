using System;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class DelegateFieldOffsetResolver(
    ITypeFinder types,
    IFieldRepository fields,
    ManagedTypeLayoutBuildState state) : IDelegateFieldOffsetResolver
{
    private readonly ITypeFinder _types = types ??
        throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ManagedTypeLayoutBuildState _state = state ??
        throw new ArgumentNullException(nameof(state));

    public ManagedDelegateFieldOffsets Resolve()
    {
        var delegateType = _types.FindType("System.Delegate");
        var fields = delegateType.Fields
            .Select(_fields.GetField)
            .Where(field => _state.ValueLayouts.Fields.ContainsKey(field.Key))
            .ToDictionary(field => field.Name,
                field => _state.ValueLayouts.Fields[field.Key],
                StringComparer.Ordinal);
        return fields.Count == 0
            ? new ManagedDelegateFieldOffsets(-1, -1, -1, -1)
            : new ManagedDelegateFieldOffsets(
                ResolveOffset(fields, "Target", "_target"),
                ResolveOffset(fields, "MethodId"),
                ResolveOffset(fields, "Left"),
                ResolveOffset(fields, "Right"));
    }

    private static int ResolveOffset(
        Dictionary<string, FieldLayout> fields,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetValue(name, out var layout))
            {
                return layout.Offset;
            }
        }

        throw new InvalidOperationException(
            $"The delegate ABI field '{names[0]}' was not found.");
    }
}
