// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ExceptionMetadataAbiTests
{
    [Fact]
    public void JavaScriptOracleReadsFixedWidthClausesForBothMemoryTargets()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var runner = new QualifiedProcessRunner();
        var script = Path.Combine(
            environment.RepositoryRoot,
            "tests",
            "NetWasm.Compiler.Tests",
            "Correctness",
            "exception-metadata.test.mjs");

        var result = runner.Run(new(
            "node",
            ["--test", script],
            environment.ProcessTimeout), TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            result.StandardOutput + result.StandardError);
    }
}
