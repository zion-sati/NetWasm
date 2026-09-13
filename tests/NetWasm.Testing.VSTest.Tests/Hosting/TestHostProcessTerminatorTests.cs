using System.ComponentModel;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class TestHostProcessTerminatorTests
{
    [Fact]
    public void KillsAndWaitsForAnActiveProcess()
    {
        var calls = new List<string>();

        new TestHostProcessTerminator().Terminate(
            () => false,
            () => calls.Add("kill"),
            () => calls.Add("wait"));

        Assert.Equal(["kill", "wait"], calls);
    }

    [Fact]
    public void LeavesAnExitedProcessUntouched()
    {
        new TestHostProcessTerminator().Terminate(
            () => true,
            () => throw new Xunit.Sdk.XunitException("Must not kill."),
            () => throw new Xunit.Sdk.XunitException("Must not wait."));
    }

    [Theory]
    [InlineData("state")]
    [InlineData("native")]
    public void NormalizesTerminalNativeProcessFailures(string failure)
    {
        var waited = false;

        new TestHostProcessTerminator().Terminate(
            () => false,
            () =>
            {
                if (failure == "state")
                {
                    throw new InvalidOperationException();
                }
                throw new Win32Exception();
            },
            () => waited = true);

        Assert.False(waited);
    }

    [Fact]
    public void RejectsMissingProcessCapabilities()
    {
        var subject = new TestHostProcessTerminator();

        Assert.Throws<ArgumentNullException>(() => subject.Terminate(null!, () => { }, () => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Terminate(() => false, null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Terminate(() => false, () => { }, null!));
    }
}
