// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Each C# fixture's NetWasm side compiles with Roslyn's runtime-async code generation, as every NetWasm project does,
// so the corpus checks the runtime async lowering against desktop .NET running the same source with its state
// machines. NETWASM_CORPUS_RUNTIME_ASYNC=0 compiles it with state machines instead, as a prebuilt library's are, which
// NetWasm still compiles through the builders. A fixture whose oracle mode shares one assembly between the
// two sides compiles that assembly with runtime async, so desktop .NET's runtime async runs the same IL.
internal sealed record CorpusRuntimeAsyncOverride(bool Enabled)
{
    public static CorpusRuntimeAsyncOverride Parse(string? value) => value switch
    {
        null or "" or "1" => new(Enabled: true),
        "0" => new(Enabled: false),
        _ => throw new InvalidOperationException("NETWASM_CORPUS_RUNTIME_ASYNC must be 0 or 1"),
    };
}
