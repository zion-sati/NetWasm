// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

public sealed class SystemProcessEnvironmentTargetTests
{
    [Fact]
    public void RemoveByPrefixIsCaseInsensitiveAndPreservesUnrelatedVariables()
    {
        var environment = new Dictionary<string, string?>
        {
            ["NODE_OPTIONS"] = "--inspect",
            ["node_path"] = "/modules",
            ["NPM_CONFIG_CACHE"] = "/cache",
            ["PATH"] = "/tools",
        };
        var target = Assert.IsAssignableFrom<IProcessEnvironmentTarget>(
            new SystemProcessEnvironmentTarget(environment));

        target.RemoveByPrefix("node_");
        target.RemoveByPrefix("npm_config_");

        Assert.Single(environment);
        Assert.Equal("/tools", environment["PATH"]);
    }

    [Fact]
    public void RemoveByPrefixRejectsMissingInputsBeforeMutation()
    {
        var environment = new Dictionary<string, string?> { ["NODE_OPTIONS"] = "value" };
        var target = new SystemProcessEnvironmentTarget(environment);

        Assert.Throws<ArgumentNullException>(() => new SystemProcessEnvironmentTarget(null!));
        Assert.Throws<ArgumentException>(() => target.RemoveByPrefix(" "));
        Assert.Equal("value", environment["NODE_OPTIONS"]);
    }
}
