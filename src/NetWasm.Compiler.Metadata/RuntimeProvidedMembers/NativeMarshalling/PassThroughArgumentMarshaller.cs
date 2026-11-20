// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using static NetWasm.Compiler.Metadata.RuntimeProvidedMembers.CilBodyElements;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A parameter the native signature takes as it is: a scalar, pointer, by-value struct or scalar by-reference.
internal static class PassThroughArgumentMarshaller
{
    public static NativeArgumentStreams Plan(int argument) => new([], [], [Argument(argument)], [], []);
}
