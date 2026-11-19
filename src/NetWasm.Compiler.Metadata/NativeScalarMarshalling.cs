// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// How desktop .NET's runtime marshalling passes a scalar P/Invoke position on Unix, which a marshalling stub repeats:
// - bool as a 4-byte BOOL, or a byte with [MarshalAs(UnmanagedType.U1 or I1)], true as 1; a native value is true
//   when it is not zero. VariantBool is Windows' and fails there, as on desktop .NET on Unix.
// - char as a UTF-16 code unit under CharSet.Unicode or [MarshalAs(UnmanagedType.U2 or I2)], and otherwise
//   (CharSet.Ansi, Auto or none, or U1 or I1) as one ANSI byte: the first byte of the character's UTF-8 encoding,
//   and back, the character a byte decodes to, U+FFFD past ASCII.
// - another primitive as itself, with a [MarshalAs] that restates its size.
internal static class NativeScalarMarshalling
{
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity Int16 = CliTypeIdentity.Primitive("i2", CliValueKind.I4);
    private static readonly CliTypeIdentity UInt16 = CliTypeIdentity.Primitive("u2", CliValueKind.I4);
    private static readonly CliTypeIdentity SByte = CliTypeIdentity.Primitive("i1", CliValueKind.I4);
    private static readonly CliTypeIdentity Byte = CliTypeIdentity.Primitive("u1", CliValueKind.I4);

    // Whether runtime marshalling converts a value of the type: bool and char.
    public static bool Converts(CliTypeIdentity type) => type.CanonicalName is "primitive:bool" or "primitive:char";

    // The native form of a bool or char position, or null for another type or a [MarshalAs] it cannot take.
    public static NativeScalarForm? Form(CliTypeIdentity type, UnmanagedType? marshalAs, MethodImportAttributes attributes) =>
        type.CanonicalName switch
        {
            "primitive:bool" => marshalAs switch
            {
                null or UnmanagedType.Bool => new(Int32, NativeScalarConversion.Boolean),
                UnmanagedType.U1 => new(Byte, NativeScalarConversion.Boolean),
                UnmanagedType.I1 => new(SByte, NativeScalarConversion.Boolean),
                _ => null,
            },
            "primitive:char" => marshalAs switch
            {
                UnmanagedType.U2 => new(UInt16, NativeScalarConversion.None),
                UnmanagedType.I2 => new(Int16, NativeScalarConversion.None),
                UnmanagedType.U1 => new(Byte, NativeScalarConversion.AnsiChar),
                UnmanagedType.I1 => new(SByte, NativeScalarConversion.AnsiChar),
                null when (attributes & MethodImportAttributes.CharSetMask) == MethodImportAttributes.CharSetUnicode =>
                    new(UInt16, NativeScalarConversion.None),
                null => new(Byte, NativeScalarConversion.AnsiChar),
                _ => null,
            },
            _ => null,
        };

    // Why a primitive position cannot take its [MarshalAs], in desktop .NET's words, or null when it can.
    public static string? Mismatch(CliTypeIdentity type, UnmanagedType? marshalAs)
    {
        if (marshalAs is null)
            return null;
        return type.CanonicalName switch
        {
            "primitive:bool" => marshalAs is UnmanagedType.Bool or UnmanagedType.U1 or UnmanagedType.I1
                ? null
                : "Invalid managed/unmanaged type combination (booleans must be paired with I1, U1, or Bool).",
            "primitive:char" => marshalAs is UnmanagedType.U1 or UnmanagedType.I1 or UnmanagedType.U2 or UnmanagedType.I2
                ? null
                : "Invalid managed/unmanaged type combination (chars must be paired with U1, I1, U2 or I2).",
            "primitive:i1" or "primitive:u1" => Restates(marshalAs, UnmanagedType.I1, UnmanagedType.U1),
            "primitive:i2" or "primitive:u2" => Restates(marshalAs, UnmanagedType.I2, UnmanagedType.U2),
            "primitive:i4" or "primitive:u4" => Restates(marshalAs, UnmanagedType.I4, UnmanagedType.U4),
            "primitive:i8" or "primitive:u8" => Restates(marshalAs, UnmanagedType.I8, UnmanagedType.U8),
            "primitive:f4" => Restates(marshalAs, UnmanagedType.R4, UnmanagedType.R4),
            "primitive:f8" => Restates(marshalAs, UnmanagedType.R8, UnmanagedType.R8),
            "primitive:nativeint" or "primitive:nativeuint" => Restates(marshalAs, UnmanagedType.SysInt, UnmanagedType.SysUInt),
            _ => null,
        };

        static string? Restates(UnmanagedType? marshalAs, UnmanagedType first, UnmanagedType second) =>
            marshalAs == first || marshalAs == second ? null : "Invalid managed/unmanaged type combination.";
    }
}

// The native type a bool or char position passes as, and how its value converts.
internal sealed record NativeScalarForm(CliTypeIdentity Native, NativeScalarConversion Conversion);

internal enum NativeScalarConversion
{
    // The value passes as it is: a char as its UTF-16 code unit.
    None,
    // true as 1 and false as 0; a native value is true when it is not zero.
    Boolean,
    // A char as one ANSI byte, and back.
    AnsiChar,
}
