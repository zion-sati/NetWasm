// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A struct that converts (NativeStructLayout), passed as desktop .NET passes it: its native copy, filled from the
// caller's value before the call. By value the function takes the copy as the Wasm C ABI passes a struct, its single
// scalar or its address, and the caller's value never changes, as desktop .NET's by-value pass is in only. By
// reference the function takes the copy's address; in, the caller's value fills it, and out, it copies back into the
// caller's variable after the call. An out-only copy starts zeroed, so the function reads no value.
internal static class StructArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, INativeStructCopier copier, CliTypeIdentity native,
        bool byReference, bool isIn, bool isOut)
    {
        var buffer = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        ImmutableArray<CilBodyElement> managed = byReference
            ? [Argument(argument)]
            : [Op(CilOperation.LoadArgumentAddress, new CilOperand.Index(argument))];
        ImmutableArray<CilBodyElement> load = native.CanonicalName == "primitive:nativeint"
            ? [Load(buffer)]
            : [Load(buffer), Op(CilOperation.LoadObject, new CilOperand.TypeIdentity(native))];
        return new(
            Locals: [buffer],
            Marshal:
            [
                .. NativeStructBuffer.Allocate(buffer, copier.NativeSize, zero: !isIn),
                .. isIn ? copier.Copy(managed, buffer, NativeCopyDirection.ToNative) : [],
            ],
            Load: load,
            Unmarshal: byReference && isOut ? copier.Copy(managed, buffer, NativeCopyDirection.ToManaged) : [],
            Cleanup: []);
    }
}
