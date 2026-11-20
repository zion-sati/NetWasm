// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// An array of blittable elements passed by value is pinned and passes its first element's address: null for a null
// array and an address even for an empty one. Native writes are visible to the caller whatever its [In] and [Out]
// attributes say, as desktop .NET's ILNativeArrayMarshaler pins it. The array is also kept alive until the call returns.
internal static class PinnedArrayArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeMarshallingMembers members)
    {
        var pinned = new CilBodyLocal(
            CliTypeIdentity.ManagedByReference(CliTypeIdentity.Primitive("u1", CliValueKind.I4)), pinned: true);
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
                Call(members.ArrayDataReference),
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
