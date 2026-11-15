// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public abstract class EmittedAssemblyTestBase(CorrectnessTestRunner runner)
{
    private protected void Run(CorpusCaseManifest manifest, string cell, IEmittedAssemblyBuilder builder, int? input = null) =>
        runner.Run(manifest, cell, builder, input);
}
