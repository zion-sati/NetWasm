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
        var functions = world.Imports.Concat(world.Exports)
            .SelectMany(item => item.Function is not null
                ? [(Function: item.Function, Owner: $"world '{world.Name}'")]
                : document.Interfaces[item.InterfaceId!.Value].Functions
                    .Select(function => (Function: function, Owner: $"interface '{item.Name}'")));
        var asynchronous = functions.FirstOrDefault(static entry =>
            entry.Function.Kind.Name.StartsWith("async", StringComparison.Ordinal));
        if (asynchronous.Function is not null)
        {
            throw ComponentException.Invalid(
                $"user-defined asynchronous WIT function '{asynchronous.Function.Name}' in {asynchronous.Owner} " +
                "is not supported");
        }
        var unsupportedType = document.Types.FirstOrDefault(type =>
            type.Kind.ValueKind == System.Text.Json.JsonValueKind.Object &&
            type.Kind.EnumerateObject().Any(kind => kind.Name is "future" or "stream"));
        if (unsupportedType is not null)
        {
            throw ComponentException.Invalid(
                $"user-defined WIT future and stream types are not supported ({Describe(document, unsupportedType)})");
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

    // A type by its name and owner, or by where an anonymous one is used.
    private static string Describe(WitDocument document, WitTypeDefinition type) =>
        (type.Name, type.OwnerInterface, type.OwnerWorld) switch
        {
            ({ } name, { } owner, _) => $"type '{name}' in interface '{document.Interfaces[owner].Key}'",
            ({ } name, _, { } owner) => $"type '{name}' in world '{document.Worlds[owner].Name}'",
            _ => $"an anonymous type, {type.Kind.EnumerateObject().First().Name}",
        };
}
