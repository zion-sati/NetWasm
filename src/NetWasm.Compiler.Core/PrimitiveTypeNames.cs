using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace NetWasm.Compiler.Core;

/// <summary>The CoreLib type definition each primitive type identity stands for.</summary>
public static class PrimitiveTypeNames
{
    private static readonly FrozenDictionary<string, string> FullNames = new Dictionary<string, string>
    {
        ["primitive:void"] = "System.Void",
        ["primitive:bool"] = "System.Boolean",
        ["primitive:char"] = "System.Char",
        ["primitive:i1"] = "System.SByte",
        ["primitive:u1"] = "System.Byte",
        ["primitive:i2"] = "System.Int16",
        ["primitive:u2"] = "System.UInt16",
        ["primitive:i4"] = "System.Int32",
        ["primitive:u4"] = "System.UInt32",
        ["primitive:i8"] = "System.Int64",
        ["primitive:u8"] = "System.UInt64",
        ["primitive:f4"] = "System.Single",
        ["primitive:f8"] = "System.Double",
        ["primitive:nativeint"] = "System.IntPtr",
        ["primitive:nativeuint"] = "System.UIntPtr",
        ["primitive:string"] = "System.String",
        ["primitive:object"] = "System.Object",
        ["primitive:typedref"] = "System.TypedReference",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The full name of the type definition a primitive's canonical name stands for, if it is one.</summary>
    public static bool TryGetFullName(string canonicalName, [NotNullWhen(true)] out string? fullName) =>
        FullNames.TryGetValue(canonicalName, out fullName);
}
