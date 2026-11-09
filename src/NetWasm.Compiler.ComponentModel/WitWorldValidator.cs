using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.ComponentModel;

public interface IWitWorldValidator
{
    void Validate(WitDocument document, WitWorld world);
}

public sealed class WitWorldValidator : IWitWorldValidator
{
    private const int ComponentModelMaximumFlagCount = 32;

    public void Validate(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        // Each placement of an interface is its own instance with its own
        // resource types, which one set of bindings cannot tell apart.
        foreach (var items in (ImmutableArray<WitWorldItem>[])[world.Imports, world.Exports])
        {
            var repeated = items.Where(static item => item.InterfaceId is not null)
                .GroupBy(static item => item.InterfaceId!.Value)
                .FirstOrDefault(static group => group.Count() > 1);
            if (repeated is not null)
            {
                throw ComponentException.Invalid(
                    $"WIT interface '{document.Interfaces[repeated.Key].Key}' is placed more than once in world " +
                    $"'{world.Name}' ({string.Join(", ", repeated.Select(static item => $"'{item.Name}'"))}), " +
                    "which NetWasm does not support");
            }
        }
        var oversizedFlags = document.Types.FirstOrDefault(type =>
            type.Kind.ValueKind == System.Text.Json.JsonValueKind.Object &&
            type.Kind.TryGetProperty("flags", out var flags) &&
            flags.GetProperty("flags").GetArrayLength() > ComponentModelMaximumFlagCount);
        if (oversizedFlags is not null)
        {
            throw ComponentException.Invalid(
                $"WIT flags type '{oversizedFlags.Name ?? $"type-{oversizedFlags.Id}"}' " +
                $"has more than the Component Model maximum of " +
                $"{ComponentModelMaximumFlagCount} flags");
        }
    }
}
