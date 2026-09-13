using System;
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
        var functions = world.Imports.Concat(world.Exports)
            .SelectMany(item => item.Function is not null
                ? [item.Function]
                : document.Interfaces[item.InterfaceId!.Value].Functions);
        var asynchronous = functions.FirstOrDefault(function =>
            function.Kind.Name.StartsWith("async", StringComparison.Ordinal));
        if (asynchronous is not null)
        {
            throw ComponentException.Invalid(
                $"user-defined asynchronous WIT function '{asynchronous.Name}' is not supported");
        }
        var unsupportedType = document.Types.FirstOrDefault(type =>
            type.Kind.ValueKind == System.Text.Json.JsonValueKind.Object &&
            type.Kind.EnumerateObject().Any(kind => kind.Name is "future" or "stream"));
        if (unsupportedType is not null)
        {
            throw ComponentException.Invalid(
                "user-defined WIT future and stream types are not supported");
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
