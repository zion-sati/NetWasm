using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

/// <summary>
/// <c>Span&lt;T&gt;</c> and <c>ReadOnlySpan&lt;T&gt;</c> of the primitive numbers
/// that <see cref="JavaScriptValueArray"/> supports. A synchronous JSImport
/// passes one to JavaScript as a typed array over linear memory, valid only
/// during the call, so nothing is copied. Each is named <c>&lt;scalar&gt;-view</c>.
/// </summary>
public static class JavaScriptMemoryView
{
    private static readonly ImmutableDictionary<string, string> Elements =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["primitive:u1"] = "u8-view",
            ["primitive:i1"] = "i8-view",
            ["primitive:i2"] = "i16-view",
            ["primitive:u2"] = "u16-view",
            ["primitive:i4"] = "i32-view",
            ["primitive:u4"] = "u32-view",
            ["primitive:i8"] = "i64-view",
            ["primitive:u8"] = "u64-view",
            ["primitive:f4"] = "f32-view",
            ["primitive:f8"] = "f64-view",
        }.ToImmutableDictionary(StringComparer.Ordinal);

    /// <summary>The ABI names of every supported view, in ordinal order.</summary>
    public static ImmutableArray<string> AbiTypes { get; } =
        [.. Elements.Values.OrderBy(name => name, StringComparer.Ordinal)];

    public static bool IsSupported(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.GenericInstantiation
            && type.ElementType!.FullName is "System.Span`1" or "System.ReadOnlySpan`1"
            && type.TypeArguments.Length == 1
            && Elements.ContainsKey(type.TypeArguments[0].CanonicalName);
    }

    public static string GetAbiType(CliTypeIdentity type) =>
        IsSupported(type)
            ? Elements[type.TypeArguments[0].CanonicalName]
            : throw new ArgumentException(
                $"'{type.CanonicalName}' is not a span of primitive numbers.", nameof(type));
}
