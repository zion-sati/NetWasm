// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeModuleMaterializer
{
    RuntimeMaterialization Materialize(RuntimeNativeMaterializationRequest request);
}

internal sealed record RuntimeNativeMaterializationRequest(
    RuntimeMaterializationRequest Consumer,
    RuntimePackManifest Manifest,
    RuntimePackTarget Target,
    RuntimeLayout SourceLayout,
    ImmutableArray<string> SystemLibraryPaths);
