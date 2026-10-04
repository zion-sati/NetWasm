using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetWasm.Hosting.Deployment;

public sealed record WitWorkerValueLayout(
    string Name, string Kind, JsonElement Value, bool Nullable, string? NumericArray);

public interface IWitWorkerValueLayoutPlanner
{
    ImmutableDictionary<int, WitWorkerValueLayout> Plan(WitWorkerResolvedContract contract);
}

/// <summary>Memoizes Jco representation choices; aliases retain names but not different value semantics.</summary>
public sealed class WitWorkerValueLayoutPlanner : IWitWorkerValueLayoutPlanner
{
    private static readonly ImmutableDictionary<string, string> NumericArrays =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["s8"] = "Int8Array",
            ["u8"] = "Uint8Array",
            ["s16"] = "Int16Array",
            ["u16"] = "Uint16Array",
            ["s32"] = "Int32Array",
            ["u32"] = "Uint32Array",
            ["s64"] = "BigInt64Array",
            ["u64"] = "BigUint64Array",
            ["f32"] = "Float32Array",
            ["f64"] = "Float64Array",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public ImmutableDictionary<int, WitWorkerValueLayout> Plan(WitWorkerResolvedContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var layouts = new Dictionary<int, WitWorkerValueLayout>();
        foreach (var id in contract.Definitions.Keys) Resolve(id);
        return layouts.ToImmutableDictionary();

        WitWorkerValueLayout Resolve(int id)
        {
            if (layouts.TryGetValue(id, out var existing)) return existing;
            var type = contract.Definitions[id];
            var kind = type.Kind.ValueKind == JsonValueKind.String
                ? type.Kind.GetString()!
                : type.Kind.EnumerateObject().Single().Name;
            var value = type.Kind.ValueKind == JsonValueKind.String ? type.Kind : type.Kind.GetProperty(kind);
            var nullable = kind switch
            {
                "type" => Nullable(contract.References[id][0]),
                "option" => !Nullable(contract.References[id][0]),
                _ => false,
            };
            var numeric = kind == "type" ? Numeric(contract.References[id][0]) : null;
            var name = type.Name is null ? $"Type{id}"
                : $"{Identifier(type.Name)}_{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(type.Owner!.Identity + "#" + type.Name)))[..12]}";
            var result = new WitWorkerValueLayout(name, kind, value, nullable, numeric);
            layouts.Add(id, result);
            return result;
        }
        bool Nullable(WitWorkerValueReference reference) => reference.Definition is { } id && Resolve(id).Nullable;
        string? Numeric(WitWorkerValueReference reference) => reference.Definition is { } id
            ? Resolve(id).NumericArray
            : NumericArrays.GetValueOrDefault(reference.Primitive!);
    }

    public static string? NumericArray(WitWorkerValueReference reference, ImmutableDictionary<int, WitWorkerValueLayout> layouts) =>
        reference.Definition is { } id ? layouts[id].NumericArray : NumericArrays.GetValueOrDefault(reference.Primitive!);

    public static WitWorkerValueReference ReadReference(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? new("primitive", value.GetString()!, null) : new("defined", null, value.GetInt32());

    public static string MemberName(string name)
    {
        // WIT segments are ASCII and uniformly cased. Jco's lower-camel
        // conversion lowercases each segment, then capitalizes later ones.
        var source = new StringBuilder();
        var upper = false;
        foreach (var character in name)
        {
            if (character == '-') upper = true;
            else { source.Append(upper ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character)); upper = false; }
        }
        return source.ToString();
    }

    private static string Identifier(string name) =>
        "Wit" + new string(name.Where(char.IsLetterOrDigit).ToArray());
}
