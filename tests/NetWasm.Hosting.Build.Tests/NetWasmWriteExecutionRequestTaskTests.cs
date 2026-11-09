using NetWasm.Hosting.Build.Execution;
using NetWasm.Hosting.Build.MsBuild;
using NetWasm.Hosting.Execution;

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
        Assert.True(task.WorkingDirectory);
        Assert.True(task.TemporaryDirectory);
    }

    // dotnet run and dotnet test mount the working and a temporary directory unless opted out.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WritesTheWorkingAndTemporaryDirectoryGrants(bool workingDirectory, bool temporaryDirectory)
    {
        var writer = new CapturingWriter();
        var task = new NetWasmWriteExecutionRequestTask(writer)
        {
            OutputPath = "/out/request.json",
            BuildFingerprint = new string('a', 64),
            DeploymentManifestSha256 = new string('b', 64),
            WorkingDirectory = workingDirectory,
            TemporaryDirectory = temporaryDirectory,
        };

        Assert.True(task.Execute());
        Assert.Equal("/out/request.json", writer.Path);
        Assert.Equal(workingDirectory, writer.Request!.Grants.WorkingDirectory);
        Assert.Equal(temporaryDirectory, writer.Request.Grants.TemporaryDirectory);
    }

    private sealed class CapturingWriter : IExecutionRequestBuildWriter
    {
        public string? Path { get; private set; }

        public NetWasmExecutionRequest? Request { get; private set; }

        public void Write(string outputPath, NetWasmExecutionRequest request)
        {
            Path = outputPath;
            Request = request;
        }
    }
}
