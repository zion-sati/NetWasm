// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

/// <summary>
/// An <c>ArraySegment&lt;T&gt;</c> of the primitive numbers <see cref="JavaScriptMemoryView"/> supports, passed to a
/// JSImport as .NET's <c>MemoryView</c>: CoreLib gives JavaScript a view of the array that keeps it alive until
/// JavaScript disposes or collects the view, so an asynchronous import can take one. It crosses as the handle value
/// <c>memory-view</c>, and only an import's parameter takes it.
/// </summary>
public static class JavaScriptArraySegment
{
    public const string AbiType = "memory-view";

    // Each element's CoreLib conversion is From<name>Segment.
    private static readonly ImmutableDictionary<string, string> Elements =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            Element("primitive:u1", "Byte"),
            Element("primitive:i1", "SByte"),
            Element("primitive:i2", "Int16"),
            Element("primitive:u2", "UInt16"),
            Element("primitive:i4", "Int32"),
            Element("primitive:u4", "UInt32"),
            Element("primitive:i8", "Int64"),
            Element("primitive:u8", "UInt64"),
            Element("primitive:f4", "Single"),
            Element("primitive:f8", "Double"),
        });

    public static bool IsSupported(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.GenericInstantiation
            && type.ElementType!.FullName == "System.ArraySegment`1"
            && type.TypeArguments.Length == 1
            && Elements.ContainsKey(type.TypeArguments[0].CanonicalName);
    }

    /// <summary>The suffix of the segment's conversion, <c>ByteSegment</c> for <c>ArraySegment&lt;byte&gt;</c>.</summary>
    public static string ConversionSuffix(CliTypeIdentity type) =>
        IsSupported(type)
            ? Elements[type.TypeArguments[0].CanonicalName] + "Segment"
            : throw new ArgumentException(
                $"'{type.CanonicalName}' is not an ArraySegment of a primitive number.", nameof(type));

    private static System.Collections.Generic.KeyValuePair<string, string> Element(string canonical, string name) =>
        new(canonical, name);
}
