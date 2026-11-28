// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers.RuntimeAsync;

internal interface IRuntimeAsyncBodyBuilder
{
    // The body a runtime-async method runs, lowered from its decoded body.
    CilMethodBody Build(MethodInstanceModel method, CilMethodBody body);
}
