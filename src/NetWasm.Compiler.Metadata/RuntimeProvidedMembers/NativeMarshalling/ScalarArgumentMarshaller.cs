// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A bool or char parameter, converted to its native form as the call loads it.
internal static class ScalarArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument, NativeScalarForm form, NativeScalarConversions conversions) =>
        new([], [], [Argument(argument), .. conversions.ToNative(form)], [], []);
}
