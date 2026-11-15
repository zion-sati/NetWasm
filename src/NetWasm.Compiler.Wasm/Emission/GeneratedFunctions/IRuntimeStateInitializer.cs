// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IRuntimeStateInitializer
{
    void Initialize(
        GeneratedFunctionWriterLease code,
        RuntimeInitializationPlan plan);
}
