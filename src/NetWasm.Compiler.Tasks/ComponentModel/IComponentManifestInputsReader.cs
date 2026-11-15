// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tasks.ComponentModel;

internal interface IComponentManifestInputsReader
{
    ComponentManifestInputs Read(ComponentManifestInputsRequest request);
}

internal sealed record ComponentManifestInputsRequest(
    string InteropManifestPath,
    string Target,
    string? JcoVersion,
    string? Preview3ShimVersion);
