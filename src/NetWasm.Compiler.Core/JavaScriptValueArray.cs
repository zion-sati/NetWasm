using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

/// <summary>
/// One-dimensional arrays of primitive numbers, which cross the JavaScript
/// interop boundary as copied typed arrays. <c>byte[]</c> keeps its established
/// <c>bytes</c> ABI name; every other element type is named <c>&lt;scalar&gt;[]</c>.
/// </summary>
public static class JavaScriptValueArray
{
    private static readonly ImmutableDictionary<string, (string AbiType, string ElementTypeName)> Elements =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["primitive:u1"] = ("bytes", "System.Byte"),
            ["primitive:i1"] = ("i8[]", "System.SByte"),
            ["primitive:i2"] = ("i16[]", "System.Int16"),
            ["primitive:u2"] = ("u16[]", "System.UInt16"),
            ["primitive:i4"] = ("i32[]", "System.Int32"),
            ["primitive:u4"] = ("u32[]", "System.UInt32"),
            ["primitive:i8"] = ("i64[]", "System.Int64"),
            ["primitive:u8"] = ("u64[]", "System.UInt64"),
            ["primitive:f4"] = ("f32[]", "System.Single"),
            ["primitive:f8"] = ("f64[]", "System.Double"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static bool IsSupported(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.SzArray
            && type.ElementType is not null
            && Elements.ContainsKey(type.ElementType.CanonicalName);
    }

    public static string GetAbiType(CliTypeIdentity type) => Require(type).AbiType;

    /// <summary>The element's metadata name, which reachability retains for the array.</summary>
    public static string GetElementTypeName(CliTypeIdentity type) => Require(type).ElementTypeName;

    private static (string AbiType, string ElementTypeName) Require(CliTypeIdentity type) =>
        IsSupported(type)
            ? Elements[type.ElementType!.CanonicalName]
            : throw new ArgumentException(
                $"'{type.CanonicalName}' is not an array of primitive numbers.", nameof(type));
}
