// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>The infrastructure-level completion of one NetWasm execution.</summary>
public enum NetWasmCompletionKind
{
    Normal,
    ManagedFailure,
    ManagedCancellation,
    CallerCancellation,
    ContractFailure,
    HostFailure,
}
