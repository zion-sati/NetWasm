// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeExceptionHandlingTranslator
{
    void Translate(RuntimeExceptionHandlingTranslationRequest request);
}

// A linked module whose native code uses Wasm exception handling's legacy encoding, the profile's features, and
// Binaryen's wasm-opt to rewrite it in place with.
internal sealed record RuntimeExceptionHandlingTranslationRequest(
    string WasmOptPath,
    string ModulePath,
    ImmutableArray<string> Features,
    string LogPath);
