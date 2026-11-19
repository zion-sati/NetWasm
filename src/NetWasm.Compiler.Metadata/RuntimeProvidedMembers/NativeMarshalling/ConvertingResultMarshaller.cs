// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A bool or char result: the native value converts to its managed form right after the call.
internal static class ConvertingResultMarshaller
{
    public static NativeResultStreams Plan(CliTypeIdentity type, NativeScalarForm form, NativeScalarConversions conversions)
    {
        var result = new CilBodyLocal(type);
        return new([result], [], [.. conversions.ToManaged(form), Store(result)], [Load(result)]);
    }
}
