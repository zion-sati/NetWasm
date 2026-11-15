// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class ManagedHeapSampler : IManagedHeapSampler
{
    public long Sample() => GC.GetTotalMemory(forceFullCollection: false);
}
