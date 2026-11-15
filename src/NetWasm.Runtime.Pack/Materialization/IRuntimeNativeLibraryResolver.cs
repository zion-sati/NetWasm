// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeNativeLibraryResolver
{
    ImmutableArray<RuntimeNativeLibrary> Resolve(RuntimeNativeLibraryResolutionRequest request);
}

internal sealed record RuntimeNativeLibraryResolutionRequest(
    string Target,
    ImmutableArray<RuntimeNativeImport> Imports,
    ImmutableArray<RuntimeNativeLibraryDescriptor> Providers);
