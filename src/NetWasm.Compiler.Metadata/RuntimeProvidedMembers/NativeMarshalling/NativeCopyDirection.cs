// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.NativeMarshalling;

// Which way a struct copy runs (INativeStructCopier): into the native copy before the call, or back out of it after.
internal enum NativeCopyDirection
{
    ToNative,
    ToManaged,
}
