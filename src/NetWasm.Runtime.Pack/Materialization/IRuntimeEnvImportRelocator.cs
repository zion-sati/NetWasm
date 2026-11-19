// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeEnvImportRelocator
{
    byte[] Relocate(ReadOnlyMemory<byte> module, ImmutableArray<RuntimeEnvImport> imports);
}

// An Emscripten JavaScript function a plain core module's JavaScript supplies (<NetWasmEnvImport>): native code imports
// it from env, and the final module imports it from the project's import module instead.
internal sealed record RuntimeEnvImport(string Name, string Module);
