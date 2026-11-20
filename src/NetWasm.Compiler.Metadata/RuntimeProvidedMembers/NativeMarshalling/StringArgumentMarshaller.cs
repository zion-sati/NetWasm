// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A string passed by value, as desktop .NET's string marshalers pass it on Unix:
// - UTF-8: a NUL-terminated copy in native memory, null for a null string, freed in the stub's finally once the call
//   returns, as CSTRMarshaler and UTF8Marshaler free it. Native code must not keep it.
// - UTF-16: the string pinned, passing its first character's address, as desktop .NET's WSTRBufferMarshaler pins it:
//   its characters are followed by a NUL, as CoreCLR's are, and native writes show in the string; null for a null
//   string. The string is also kept alive until the call returns.
internal static class StringArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeStringForm form, NativeMarshallingMembers members) =>
        form == NativeStringForm.Utf8 ? Utf8(argument, members) : Utf16(argument, members);

    private static NativeArgumentStreams Utf8(int argument, NativeMarshallingMembers members)
    {
        var native = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        return new(
            Locals: [native],
            Marshal: [Argument(argument), Call(members.StringToUtf8), Store(native)],
            Load: [Load(native)],
            Unmarshal: [],
            Cleanup: [Load(native), Call(members.FreeNative)]);
    }

    private static NativeArgumentStreams Utf16(int argument, NativeMarshallingMembers members)
    {
        var pinned = new CilBodyLocal(
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.Primitive("char", CliValueKind.I4)), pinned: true);
        var native = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        var done = new CilBodyLabel();
        return new(
            Locals: [pinned, native],
            Marshal:
            [
                Op(CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                Op(CilOperation.ConvertNativeUInt),
                Store(native),
                Argument(argument),
                Branch(CilOperation.BranchIfFalse, done),
                Argument(argument),
                Call(members.StringPinnableReference),
                Store(pinned),
                Load(pinned),
                Op(CilOperation.ConvertNativeUInt),
                Store(native),
                Mark(done),
            ],
            Load: [Load(native)],
            Unmarshal: [Argument(argument), Call(members.KeepAlive)],
            Cleanup: []);
    }
}
