// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal interface IUnsafeIntrinsicBodyBuilder
{
    // The body the compiler gives the Unsafe intrinsic, or null for any other method.
    CilMethodBody? Build(MethodInstanceModel method);
}
