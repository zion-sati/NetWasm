// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A result the native signature returns as it is, held across the stub's cleanup; or none for a void result.
internal static class ValueResultMarshaller
{
    public static NativeResultStreams Plan(CliTypeIdentity type)
    {
        if (type.StackKind == CliValueKind.Void)
        {
            return new([], [], [], []);
        }
        var result = new CilBodyLocal(type);
        return new([result], [], [Store(result)], [Load(result)]);
    }
}
