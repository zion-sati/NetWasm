// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Sdk.Pack.MsBuild;

public sealed record ProjectEvaluationRequest(
    string TargetFramework,
    string? Configuration = null,
    string? Platform = null,
    string? RuntimeIdentifier = null);
