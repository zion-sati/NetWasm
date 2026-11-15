// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

namespace NetWasm.Hosting.Execution;

/// <summary>An exact application module bound to a hashed deployment asset.</summary>
public sealed record NetWasmApplicationImport(string Module, string ArtifactPath, string Sha256);
