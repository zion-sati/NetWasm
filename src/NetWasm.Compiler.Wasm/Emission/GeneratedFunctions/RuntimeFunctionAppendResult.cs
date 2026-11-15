// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed record RuntimeFunctionAppendResult(
    int FilterDispatcherIndex,
    int FinalizerDispatcherIndex,
    int? EntryPointIndex,
    bool HasFinalizers);
