using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Wit.Bindings;

public interface IWitCanonicalTypeReachabilityResolver
{
    ImmutableArray<int> Resolve(WitDocument document, WitWorld world);
}

public sealed class WitCanonicalTypeReachabilityResolver :
    IWitCanonicalTypeReachabilityResolver
{
    public ImmutableArray<int> Resolve(WitDocument document, WitWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        var reachable = new HashSet<int>();
        foreach (var function in Functions(document, world))
        {
            foreach (var parameter in function.Parameters)
            {
                Add(document, reachable, parameter.Type);
            }
            if (function.Result is not null)
            {
                Add(document, reachable, function.Result);
            }
        }
        return [.. reachable.Order()];
    }

    private static void Add(
        WitDocument document,
        HashSet<int> reachable,
        WitTypeReference reference)
    {
        if (reference is not WitTypeReference.Defined defined ||
            !reachable.Add(defined.Id))
        {
            return;
        }
        var definition = document.Types[defined.Id];
        if (definition.Kind.ValueKind == JsonValueKind.String)
        {
            return;
        }
        var kind = definition.Kind.EnumerateObject().Single();
        var operations = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            ["type"] = () => Add(document, reachable, Reference(kind.Value)),
            ["list"] = () => Add(document, reachable, Reference(kind.Value)),
            ["option"] = () => Add(document, reachable, Reference(kind.Value)),
            ["record"] = () =>
            {
                foreach (var field in kind.Value.GetProperty("fields").EnumerateArray())
                {
                    Add(document, reachable, Reference(field.GetProperty("type")));
                }
            },
            ["tuple"] = () =>
            {
                foreach (var item in kind.Value.GetProperty("types").EnumerateArray())
                {
                    Add(document, reachable, Reference(item));
                }
            },
            ["result"] = () =>
            {
                AddNullable(document, reachable, kind.Value.GetProperty("ok"));
                AddNullable(document, reachable, kind.Value.GetProperty("err"));
            },
            ["variant"] = () =>
            {
                foreach (var item in kind.Value.GetProperty("cases").EnumerateArray())
                {
                    AddNullable(document, reachable, item.GetProperty("type"));
                }
            },
            ["enum"] = static () => { },
            ["flags"] = static () => { },
            ["handle"] = () => Add(document, reachable, new WitTypeReference.Defined(
                    kind.Value.EnumerateObject().Single().Value.GetInt32())),
        };
        operations[kind.Name]();
    }

    private static void AddNullable(
        WitDocument document,
        HashSet<int> reachable,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            Add(document, reachable, Reference(value));
        }
    }

    private static IEnumerable<WitFunction> Functions(
        WitDocument document,
        WitWorld world) => world.Imports.Concat(world.Exports)
        .SelectMany(item => item.Function is not null
            ? [item.Function]
            : document.Interfaces[item.InterfaceId!.Value].Functions);

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());
}
