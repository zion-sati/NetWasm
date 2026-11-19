// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using System.Runtime.InteropServices;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// How desktop .NET's runtime marshalling passes a string P/Invoke argument or result on Unix, which a marshalling stub
// repeats: as UTF-16 under CharSet.Unicode or [MarshalAs(UnmanagedType.LPWStr)], the string's own characters, which a
// NUL follows; otherwise (CharSet.Ansi, Auto or none, LPStr or LPUTF8Str) as a NUL-terminated UTF-8 copy, since ANSI is
// UTF-8 there. A null string passes a null pointer, and a null result is a null string.
internal static class NativeStringMarshalling
{
    public const string Mismatch =
        "Invalid managed/unmanaged type combination (a string must be paired with LPStr, LPWStr or LPUTF8Str).";

    public static bool IsString(CliTypeIdentity type) => type.CanonicalName == "primitive:string";

    // The string's native encoding, or null for a [MarshalAs] it cannot take.
    public static NativeStringForm? Form(UnmanagedType? marshalAs, MethodImportAttributes attributes) => marshalAs switch
    {
        UnmanagedType.LPWStr => NativeStringForm.Utf16,
        UnmanagedType.LPStr or UnmanagedType.LPUTF8Str => NativeStringForm.Utf8,
        null when (attributes & MethodImportAttributes.CharSetMask) == MethodImportAttributes.CharSetUnicode =>
            NativeStringForm.Utf16,
        null => NativeStringForm.Utf8,
        _ => null,
    };
}

internal enum NativeStringForm
{
    // A NUL-terminated UTF-8 copy.
    Utf8,
    // The string's UTF-16 characters, pinned and NUL-terminated, with native writes showing in the string.
    Utf16,
}
