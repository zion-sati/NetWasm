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
    ImmutableArray<string> SystemLibraryPaths)
{
    // The C++ libraries with Wasm exception handling, linked instead when an archive throws or catches C++ exceptions.
    public ImmutableArray<string> ExceptionSystemLibraryPaths { get; init; } = [];
    // Entry points of the C library functions the program imports, which the runtime exports for it.
    public ImmutableArray<string> SystemExports { get; init; } = [];
}
