// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class TestHostProcessAttacherTests
{
    [Fact]
    public void AttachesOnlyToTheExactExistingProcess()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        using var result = Attacher().Attach(current.Id, (_, _) => { });

        Assert.Equal(current.Id, result.Id);
    }

    [Fact]
    public void RejectsInvalidOrUnavailableProcessIdentities()
    {
        var subject = Attacher();

        Assert.Throws<ArgumentOutOfRangeException>(() => subject.Attach(0, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Attach(Environment.ProcessId, null!));
        Assert.Throws<InvalidOperationException>(() => subject.Attach(int.MaxValue, (_, _) => { }));
    }

    [Fact]
    public void RejectsAMissingTerminationStrategy()
    {
        Assert.Throws<ArgumentNullException>(() => new TestHostProcessAttacher(null!));
    }

    private static TestHostProcessAttacher Attacher() =>
        new(new TestHostProcessTerminator());
}
