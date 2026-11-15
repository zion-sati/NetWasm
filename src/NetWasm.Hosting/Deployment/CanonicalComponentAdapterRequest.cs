// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Deployment;

/// <summary>Identifies one exact generated component boundary.</summary>
public sealed record CanonicalComponentAdapterRequest(
    string ContractKey,
    string JcoVersion);
