// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Makes the copier of a struct a marshalling stub passes through its native copy.
internal interface INativeStructCopierFactory
{
    // The struct's copier, or null for a struct runtime marshalling cannot pass or whose native layout converts nothing.
    INativeStructCopier? Create(CliTypeIdentity type, NativeScalarConversions conversions);
}
