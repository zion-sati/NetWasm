// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed record ManagedMethodBodyEmission(
    byte[] Body,
    int WasmInstructionCount,
    long CompileDurationTicks,
    long? PeakObservedManagedMemoryBytes,
    string MethodKey,
    FilterEnvironmentLayout FilterEnvironment,
    ImmutableDictionary<int, int> OriginalBlockEmissionCounts);
