// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A CriticalHandle passed by value passes its handle field, which a null handle fails to read, and is kept alive until
// the call returns, as desktop .NET's ILCriticalHandleMarshaler does; it is not reference counted.
internal static class CriticalHandleArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeMarshallingMembers members) => new(
        Locals: [],
        Marshal: [],
        Load: [Argument(argument), Op(CilOperation.LoadField, new CilOperand.FieldInstance(members.HandleFields.For(HandleKind.Critical)))],
        Unmarshal: [Argument(argument), Call(members.KeepAlive)],
        Cleanup: []);
}
