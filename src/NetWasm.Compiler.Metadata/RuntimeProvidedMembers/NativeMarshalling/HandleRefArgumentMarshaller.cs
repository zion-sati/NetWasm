// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A HandleRef passes its handle, and its wrapper is kept alive until the call returns, as desktop .NET's
// ILHandleRefMarshaler does.
internal static class HandleRefArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeMarshallingMembers members) => new(
        Locals: [],
        Marshal: [],
        Load: [Op(CilOperation.LoadArgumentAddress, new CilOperand.Index(argument)), Call(members.HandleRefHandle)],
        Unmarshal:
        [
            Op(CilOperation.LoadArgumentAddress, new CilOperand.Index(argument)),
            Call(members.HandleRefWrapper),
            Call(members.KeepAlive),
        ],
        Cleanup: []);
}
