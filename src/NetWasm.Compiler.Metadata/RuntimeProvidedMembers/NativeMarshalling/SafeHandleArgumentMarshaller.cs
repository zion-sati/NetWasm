// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A SafeHandle passed by value is borrowed for the call: StubHelpers.SafeHandleAddRef rejects null and a closed handle
// before the native call, and the finally releases it only if it was added, as desktop .NET's ILSafeHandleMarshaler does.
internal static class SafeHandleArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeMarshallingMembers members)
    {
        var added = new CilBodyLocal(CliTypeIdentity.Primitive("bool", CliValueKind.I4));
        var native = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        var done = new CilBodyLabel();
        return new(
            Locals: [added, native],
            Marshal: [Argument(argument), Address(added), Call(members.SafeHandleAddRef), Store(native)],
            Load: [Load(native)],
            Unmarshal: [],
            Cleanup:
            [
                Load(added),
                Branch(CilOperation.BranchIfFalse, done),
                Argument(argument),
                Call(members.SafeHandleRelease),
                Mark(done),
            ]);
    }
}
