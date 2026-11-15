// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CfgPropertyTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StructuredAndEmittedGraphsMatchTheReferenceInterpreter()
    {
        var requested = Environment.GetEnvironmentVariable(
            "NETWASM_CFG_PROPERTY_SEED");
        var seeds = requested is null
            ? (int[])[0x31415, 0x27182]
            : (int[])[int.Parse(
                requested,
                System.Globalization.CultureInfo.InvariantCulture)];
        foreach (var property in runner.GenerateCfgProperties(seeds))
        {
            runner.RunCfgProperty(property);
        }
    }
}
