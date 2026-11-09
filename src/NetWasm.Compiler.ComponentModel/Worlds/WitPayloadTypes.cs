using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Worlds;

/// <summary>
/// The stream and future types of a function's signature in wit-parser's
/// find_futures_and_streams order: its parameters, then its result, each type's nested
/// streams and futures before itself. A built-in such as <c>[stream-new-N]f</c> names the
/// Nth.
/// </summary>
public static class WitPayloadTypes
{
    public static ImmutableArray<int> Find(WitDocument document, WitFunction function)
    {
        var found = new List<int>();
        foreach (var parameter in function.Parameters)
        {
            Find(document, parameter.Type, found);
        }
        if (function.Result is { } result)
        {
            Find(document, result, found);
        }
        return [.. found];
    }

    /// <summary>Whether a payload type is a stream (otherwise a future).</summary>
    public static bool IsStream(WitDocument document, int typeId) =>
        document.Types[typeId].Kind.EnumerateObject().Single().Name == "stream";

    private static void Find(WitDocument document, WitTypeReference reference, List<int> found)
    {
        if (reference is not WitTypeReference.Defined defined ||
            document.Types[defined.Id].Kind.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var kind = document.Types[defined.Id].Kind.EnumerateObject().Single();
        var value = kind.Value;
        if (kind.NameEquals("type") || kind.NameEquals("list") || kind.NameEquals("option"))
        {
            Find(document, Reference(value), found);
        }
        else if (kind.NameEquals("record"))
        {
            foreach (var field in value.GetProperty("fields").EnumerateArray())
            {
                Find(document, Reference(field.GetProperty("type")), found);
            }
        }
        else if (kind.NameEquals("tuple"))
        {
            foreach (var item in value.GetProperty("types").EnumerateArray())
            {
                Find(document, Reference(item), found);
            }
        }
        else if (kind.NameEquals("variant"))
        {
            foreach (var item in value.GetProperty("cases").EnumerateArray())
            {
                FindNullable(document, item.GetProperty("type"), found);
            }
        }
        else if (kind.NameEquals("result"))
        {
            FindNullable(document, value.GetProperty("ok"), found);
            FindNullable(document, value.GetProperty("err"), found);
        }
        else if (kind.NameEquals("stream") || kind.NameEquals("future"))
        {
            FindNullable(document, value, found);
            found.Add(defined.Id);
        }
    }

    private static void FindNullable(WitDocument document, JsonElement value, List<int> found)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            Find(document, Reference(value), found);
        }
    }

    private static WitTypeReference Reference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? new WitTypeReference.Primitive(value.GetString()!)
            : new WitTypeReference.Defined(value.GetInt32());
}
