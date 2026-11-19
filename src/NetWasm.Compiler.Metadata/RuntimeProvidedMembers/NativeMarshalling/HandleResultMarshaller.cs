// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A SafeHandle or CriticalHandle result: the handle is made before the call, so a failure to make it cannot leave a
// native resource nothing owns, and takes the native result as its handle, as desktop .NET's handle marshalers do.
internal static class HandleResultMarshaller
{
    public static NativeResultStreams Plan(CliTypeIdentity type, MethodInstanceModel constructor, FieldInstanceModel handle)
    {
        var result = new CilBodyLocal(type);
        var native = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        return new(
            Locals: [result, native],
            Prepare: [Op(CilOperation.NewObject, new CilOperand.MethodInstance(constructor)), Store(result)],
            Store: [Store(native), Load(result), Load(native), Op(CilOperation.StoreField, new CilOperand.FieldInstance(handle))],
            Load: [Load(result)]);
    }
}
