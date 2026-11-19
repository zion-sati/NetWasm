// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// One parameter's part of a marshalling stub: the locals it declares, then its streams in the order desktop .NET's IL
// stub runs them: marshal before the call, load the native argument for the call, unmarshal after it, and clean up in
// the finally that guards the call.
internal sealed record NativeArgumentStreams(
    ImmutableArray<CilBodyLocal> Locals,
    ImmutableArray<CilBodyElement> Marshal,
    ImmutableArray<CilBodyElement> Load,
    ImmutableArray<CilBodyElement> Unmarshal,
    ImmutableArray<CilBodyElement> Cleanup)
{
    public bool NeedsCleanup => !Cleanup.IsEmpty;
}
