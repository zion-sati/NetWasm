// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed record CompilerBuildMetadata(
    int SchemaVersion,
    string Target,
    ImmutableArray<string> RuntimeFeatures,
    ImmutableArray<WasmFunctionImport> FunctionImports);
