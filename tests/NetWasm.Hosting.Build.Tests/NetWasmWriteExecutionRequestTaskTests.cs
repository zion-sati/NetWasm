using NetWasm.Hosting.Build.MsBuild;

namespace NetWasm.Hosting.Build.Tests;

public sealed class NetWasmWriteExecutionRequestTaskTests
{
    [Fact]
    public void DefaultsToOrdinaryLocalHostCapabilities()
    {
        var task = new NetWasmWriteExecutionRequestTask();

        Assert.Equal("allowAll", task.Network);
        Assert.True(task.Randomness);
        Assert.Equal(["wall", "monotonic"], task.Clocks.Select(item => item.ItemSpec));
        Assert.Empty(task.EnvironmentVariables);
        Assert.Empty(task.Preopens);
    }
}
