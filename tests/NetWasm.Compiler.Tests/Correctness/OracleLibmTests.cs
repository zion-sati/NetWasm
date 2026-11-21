// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// The simulated runtime's libm (oracle-libm.mjs) provides exactly CoreLib's libc math imports and the musl functions
// its managed ports reproduce, built by the pinned Emscripten (eng/build-oracle-libm.sh), for both memory targets.
public sealed class OracleLibmTests
{
    [Fact]
    public void TheOracleLibmMatchesCoreLibsImportsAndThePinnedToolchain()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();
        var runner = new QualifiedProcessRunner();
        var script = Path.Combine(
            environment.RepositoryRoot,
            "tests",
            "NetWasm.Compiler.Tests",
            "Correctness",
            "oracle-libm.test.mjs");

        var result = runner.Run(new(
            "node",
            ["--test", script],
            environment.ProcessTimeout), TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            result.StandardOutput + result.StandardError);
    }
}
