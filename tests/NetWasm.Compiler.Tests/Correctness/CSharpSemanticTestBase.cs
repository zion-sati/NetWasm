// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public abstract class CSharpSemanticTestBase(CorrectnessTestRunner runner)
{
    private protected void Run(CorpusCaseManifest manifest, string cell) => runner.Run(manifest, cell);

    private protected void Run(CorpusFixture fixture) => runner.Run(fixture);
}
