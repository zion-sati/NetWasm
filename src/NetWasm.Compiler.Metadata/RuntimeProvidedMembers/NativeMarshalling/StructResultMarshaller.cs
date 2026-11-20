// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A struct result that converts (NativeStructLayout), returned as the Wasm C ABI returns it: its single scalar, which the
// stub stores into a native copy, or through a native copy whose address the stub passes first. The copy converts into
// the managed result, as desktop .NET's layout-class marshaler does.
internal static class StructResultMarshaller
{
    public static NativeResultStreams Plan(CliTypeIdentity type, INativeStructCopier copier, CliTypeIdentity? scalar)
    {
        var buffer = new CilBodyLocal(CliTypeIdentity.FromStackKind(CliValueKind.NativeInt));
        var result = new CilBodyLocal(type);
        CilBodyElement[] copyOut =
        [
            Address(result), Op(CilOperation.InitializeObject, new CilOperand.TypeIdentity(type)),
            .. copier.Copy([Address(result)], buffer, NativeCopyDirection.ToManaged),
        ];
        if (scalar is null)
        {
            return new(
                Locals: [buffer, result],
                Prepare: NativeStructBuffer.Allocate(buffer, copier.NativeSize, zero: true),
                Store: [.. copyOut],
                Load: [Load(result)])
            {
                Leading = [Load(buffer)],
            };
        }
        var value = new CilBodyLocal(scalar);
        return new(
            Locals: [buffer, result, value],
            Prepare: NativeStructBuffer.Allocate(buffer, copier.NativeSize, zero: true),
            Store: [Store(value), Load(buffer), Load(value), Op(CilOperation.StoreObject, new CilOperand.TypeIdentity(scalar)), .. copyOut],
            Load: [Load(result)]);
    }
}
