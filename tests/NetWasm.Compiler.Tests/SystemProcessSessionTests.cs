// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests;

public sealed class SystemProcessSessionTests
{
    [Fact]
    public void SessionExposesTheUnstartedProcessEnvironmentThroughItsCapability()
    {
        var process = new Process();
        process.StartInfo.Environment["NODE_OPTIONS"] = "--inspect";
        using var session = Assert.IsAssignableFrom<IProcessSession>(
            new SystemProcessSession(process));

        session.Environment.RemoveByPrefix("node_");

        Assert.DoesNotContain("NODE_OPTIONS", process.StartInfo.Environment.Keys);
        Assert.NotNull(session.Arguments);
        Assert.NotNull(session.Starter);
        Assert.NotNull(session.StandardOutput);
        Assert.NotNull(session.StandardError);
        Assert.NotNull(session.Waiter);
        Assert.NotNull(session.ExitCode);
    }

    [Fact]
    public void ConstructorRejectsANullProcess()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemProcessSession(null!));
    }
}
