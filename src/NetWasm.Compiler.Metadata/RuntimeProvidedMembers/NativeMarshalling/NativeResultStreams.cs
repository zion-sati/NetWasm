// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// A marshalling stub's result: the locals it declares; prepared before the call, stored from the native result right
// after it, and loaded for the stub's return once its cleanup has run. Leading loads the native function's first
// argument, ahead of the parameters', for a result the function returns through a pointer.
internal sealed record NativeResultStreams(
    ImmutableArray<CilBodyLocal> Locals,
    ImmutableArray<CilBodyElement> Prepare,
    ImmutableArray<CilBodyElement> Store,
    ImmutableArray<CilBodyElement> Load)
{
    public ImmutableArray<CilBodyElement> Leading { get; init; } = [];
}
