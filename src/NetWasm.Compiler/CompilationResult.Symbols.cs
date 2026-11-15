// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler;

public sealed partial record CompilationResult
{
    // <program>.wasm.type-symbols, which every compile writes (issue 225).
    public SymbolSidecarArtifact? TypeSymbols { get; init; }

    // <program>.wasm.method-symbols, with NetWasmManagedStackTrace.
    public SymbolSidecarArtifact? MethodSymbols { get; init; }
}
