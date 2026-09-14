using NetWasm.Toolchain.Prerequisites;

namespace NetWasm.Toolchain.Tests;

#pragma warning disable CA1859 // Contract test deliberately dispatches through the public interface.

public sealed class ProcessHostToolCompatibilityProbeTests
{
    [Fact]
    public void ObserveReadsTheInstalledDotNetVersionThroughTheContract()
    {
        var dotnet = ResolveDotNet();
        IHostToolCompatibilityProbe probe = new ProcessHostToolCompatibilityProbe();

        var result = probe.Observe(new("test-dotnet", dotnet, HostExecutableResolutionSource.Path));

        Assert.Equal("test-dotnet", result.ToolId);
        Assert.True(result.Version.Major >= 10);
        Assert.Empty(result.Capabilities);
    }

    [Fact]
    public void ObserveRejectsInvalidProducts()
    {
        var probe = new ProcessHostToolCompatibilityProbe();

        Assert.Throws<ArgumentNullException>(() => probe.Observe(null!));
        Assert.Throws<ArgumentException>(() => probe.Observe(new(
            "", ResolveDotNet(), HostExecutableResolutionSource.Path)));
        Assert.Throws<ArgumentException>(() => probe.Observe(new(
            "dotnet", "dotnet", HostExecutableResolutionSource.Path)));
    }

    private static string ResolveDotNet()
    {
        var hostPath = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && Path.IsPathFullyQualified(hostPath))
        {
            return hostPath;
        }

        var processPath = System.Environment.ProcessPath;
        if (processPath is not null
            && string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        throw new InvalidOperationException("The test host did not expose an absolute dotnet path.");
    }
}

#pragma warning restore CA1859
