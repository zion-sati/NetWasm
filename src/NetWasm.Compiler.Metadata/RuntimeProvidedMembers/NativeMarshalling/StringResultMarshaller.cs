// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A string result, as desktop .NET's string marshalers return one on Unix: the native NUL-terminated buffer decoded
// (UTF-8 for LPStr, LPUTF8Str and ANSI, which is UTF-8 there; UTF-16 for LPWStr and CharSet.Unicode), then freed with
// Marshal.FreeCoTaskMem, which is the C library's free, so a buffer native code allocated with malloc is released. A
// null pointer is a null string.
internal static class StringResultMarshaller
{
    public static NativeResultStreams Plan(NativeStringForm form, NativeMarshallingMembers members)
    {
        var native = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        var result = new CilBodyLocal(CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference));
        return new(
            [native, result],
            [],
            [
                Store(native),
                Load(native),
                Call(form == NativeStringForm.Utf8 ? members.PtrToStringUtf8 : members.PtrToStringUtf16),
                Store(result),
                Load(native),
                Call(members.FreeNative),
            ],
            [Load(result)]);
    }
}
