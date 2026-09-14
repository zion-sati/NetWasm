using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class SystemExternalCommandRunnerTests
{
    [Fact]
    public void RunnerExecutesAndRejectsInvalidCommands()
    {
#pragma warning disable CA1859 // Contract test deliberately dispatches through the production interface.
        IExternalToolRunner runner = new SystemExternalCommandRunner();
#pragma warning restore CA1859

        var result = runner.Run(ResolveDotNet(), ["--version"]);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.StandardOutput);
        Assert.Throws<ArgumentException>(() => runner.Run(" ", []));
        Assert.Throws<ArgumentNullException>(() => runner.Run(ResolveDotNet(), null!));
        Assert.Throws<WitBindingException>(() =>
            runner.Run("netwasm-command-that-does-not-exist", []));
    }

    private static string ResolveDotNet()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && Path.IsPathFullyQualified(hostPath))
        {
            return hostPath;
        }

        var processPath = Environment.ProcessPath;
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
