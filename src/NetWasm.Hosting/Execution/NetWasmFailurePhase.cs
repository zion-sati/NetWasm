// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>The execution phase that produced a structured failure.</summary>
public enum NetWasmFailurePhase
{
    Validation,
    Instantiation,
    Execution,
    Observation,
    Cleanup,
    Output,
}
