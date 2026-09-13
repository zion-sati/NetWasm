using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NetWasm.Testing.VSTest.Hosting;
using NetWasm.Testing.VSTest.Tests.ProcessProbe;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class TestHostProcessStarterTests
{
    [Fact]
    public async Task StartsWithoutAShellAndDrainsBothStreamsBeforePublishingExit()
    {
        var output = new ConcurrentQueue<string>();
        var error = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource<(int ProcessId, int ExitCode)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var previousRemovedValue = Environment.GetEnvironmentVariable("NETWASM_VSTEST_REMOVE");
        Environment.SetEnvironmentVariable("NETWASM_VSTEST_REMOVE", "parent");
        try
        {
            using var process = Starter().Start(
                StartInfo("emit 7", new Dictionary<string, string?>
                {
                    ["NETWASM_VSTEST_KEEP"] = "child",
                    ["NETWASM_VSTEST_REMOVE"] = null,
                }),
                output.Enqueue,
                error.Enqueue,
                (processId, exitCode) => completion.TrySetResult((processId, exitCode)));

            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(process.Id, result.ProcessId);
            Assert.Equal(7, result.ExitCode);
            Assert.Equal(["child"], output);
            Assert.Equal(["<missing>"], error);
            process.Terminate();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NETWASM_VSTEST_REMOVE", previousRemovedValue);
        }
    }

    [Fact]
    public async Task TerminatesTheActiveProcessTreeAndPublishesItsExit()
    {
        var completion = new TaskCompletionSource<(int ProcessId, int ExitCode)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = Starter().Start(
            StartInfo("wait", null),
            _ => { },
            _ => { },
            (processId, exitCode) => completion.TrySetResult((processId, exitCode)));

        process.Terminate();
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(process.Id, result.ProcessId);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void RejectsAnUnavailableExecutable()
    {
        var startInfo = new TestProcessStartInfo
        {
            FileName = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
            WorkingDirectory = Path.GetTempPath(),
        };

        Assert.Throws<InvalidOperationException>(() => Starter().Start(
            startInfo,
            _ => { },
            _ => { },
            (_, _) => { }));
    }

    [Fact]
    public void RejectsMissingStartContracts()
    {
        var subject = Starter();
        var valid = StartInfo("emit 0", null);

        Assert.Throws<ArgumentNullException>(() => new TestHostProcessStarter(null!));
        Assert.Throws<ArgumentNullException>(() => subject.Start(
            null!, _ => { }, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Start(
            valid, null!, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Start(
            valid, _ => { }, null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => subject.Start(
            valid, _ => { }, _ => { }, null!));
        Assert.Throws<ArgumentException>(() => subject.Start(
            new TestProcessStartInfo { FileName = "", WorkingDirectory = Path.GetTempPath() },
            _ => { }, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentException>(() => subject.Start(
            new TestProcessStartInfo { FileName = DotnetPath(), WorkingDirectory = "" },
            _ => { }, _ => { }, (_, _) => { }));
    }

    [Fact]
    public void ProcessResourceRejectsMissingDependenciesAndToleratesTerminationAfterDisposal()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var terminator = new TestHostProcessTerminator();
        Assert.Throws<ArgumentNullException>(() =>
            new TestHostProcess(null!, (_, _) => { }, terminator));
        Assert.Throws<ArgumentNullException>(() =>
            new TestHostProcess(current, null!, terminator));
        Assert.Throws<ArgumentNullException>(() =>
            new TestHostProcess(current, (_, _) => { }, null!));
        var process = new TestHostProcess(current, (_, _) => { }, terminator);

        process.Dispose();
        process.Terminate();
    }

    private static TestProcessStartInfo StartInfo(
        string probeArguments,
        IDictionary<string, string?>? environmentVariables)
    {
        var probePath = typeof(ProbeMarker).Assembly.Location;
        var runtimeConfigurationPath = Path.ChangeExtension(
            typeof(TestHostProcessStarterTests).Assembly.Location,
            ".runtimeconfig.json");
        return new TestProcessStartInfo
        {
            FileName = DotnetPath(),
            Arguments = $"exec --runtimeconfig \"{runtimeConfigurationPath}\" \"{probePath}\" {probeArguments}",
            WorkingDirectory = Path.GetTempPath(),
            EnvironmentVariables = environmentVariables,
        };
    }

    private static string DotnetPath()
    {
        var path = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path;
    }

    private static TestHostProcessStarter Starter() =>
        new(new TestHostProcessTerminator());
}
