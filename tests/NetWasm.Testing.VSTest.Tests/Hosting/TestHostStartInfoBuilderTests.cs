using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class TestHostStartInfoBuilderTests
{
    [Fact]
    public void BuildsThePortableTesthostCommandWithQuotedExactConnectionValues()
    {
        var root = Path.GetFullPath("test host with spaces");
        var assets = new PortableTestHostAssets(
            Path.Combine(root, "testhost.dll"),
            Path.Combine(root, "testhost.deps.json"),
            Path.Combine(root, "testhost.runtimeconfig.json"),
            Path.Combine(root, "packages"));
        var dotnet = Path.GetFullPath(Path.Combine("dotnet root", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var workingDirectory = Path.GetFullPath("working directory");
        var environment = new Dictionary<string, string?>
        {
            ["KEEP"] = "value",
            ["REMOVE"] = null,
        };
        var connection = new TestRunnerConnectionInfo
        {
            Port = 1234,
            RunnerProcessId = 5678,
            LogFile = Path.GetFullPath("logs/test host.diag"),
            TraceLevel = 4,
            ConnectionInfo = new TestHostConnectionInfo
            {
                Endpoint = "127.0.0.1:1234",
                Role = ConnectionRole.Client,
                Transport = Transport.Sockets,
            },
        };

        var result = new TestHostStartInfoBuilder().Build(
            dotnet,
            assets,
            workingDirectory,
            environment,
            connection);

        Assert.Equal(dotnet, result.FileName);
        Assert.Equal(workingDirectory, result.WorkingDirectory);
        Assert.Equal(environment, result.EnvironmentVariables);
        Assert.NotSame(environment, result.EnvironmentVariables);
        Assert.Equal(
            $"\"exec\" \"--runtimeconfig\" \"{assets.RuntimeConfigurationPath}\" "
            + $"\"--depsfile\" \"{assets.DependencyManifestPath}\" "
            + $"\"--additionalprobingpath\" \"{assets.PackageProbingPath}\" "
            + $"\"{assets.TestHostPath}\" \"--port\" \"1234\" "
            + $"\"--endpoint\" \"127.0.0.1:1234\" \"--role\" \"client\" "
            + $"\"--parentprocessid\" \"5678\" \"--diag\" \"{connection.LogFile}\" "
            + "\"--tracelevel\" \"4\"",
            result.Arguments);
    }

    [Fact]
    public void OmitsOptionalDiagnosticsAndDoesNotReuseCallerEnvironmentState()
    {
        var result = new TestHostStartInfoBuilder().Build(
            Path.GetFullPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            Assets(),
            Path.GetFullPath("work"),
            null,
            Connection());

        Assert.Empty(result.EnvironmentVariables!);
        Assert.DoesNotContain("--diag", result.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("--tracelevel", result.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsTheHostRoleAndEscapesQuotesAndTrailingBackslashes()
    {
        var connection = Connection();
        var hostConnection = connection.ConnectionInfo;
        hostConnection.Role = ConnectionRole.Host;
        hostConnection.Endpoint = "endpoint\\segment\"quoted\"\\";
        connection.ConnectionInfo = hostConnection;

        var result = new TestHostStartInfoBuilder().Build(
            Path.GetFullPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            Assets(),
            Path.GetFullPath("work"),
            null,
            connection);

        Assert.Contains("\"--role\" \"host\"", result.Arguments, StringComparison.Ordinal);
        Assert.Contains(
            "\"endpoint\\segment\\\"quoted\\\"\\\\\"",
            result.Arguments,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingRequiredInputs()
    {
        var subject = new TestHostStartInfoBuilder();
        var dotnet = Path.GetFullPath(OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var workingDirectory = Path.GetFullPath("work");
        var connection = Connection();

        Assert.Throws<ArgumentException>(() => subject.Build(
            "", Assets(), workingDirectory, null, connection));
        Assert.Throws<ArgumentNullException>(() => subject.Build(
            dotnet, null!, workingDirectory, null, connection));
        Assert.Throws<ArgumentException>(() => subject.Build(
            dotnet, Assets(), "", null, connection));
        Assert.Throws<ArgumentNullException>(() => subject.Build(
            dotnet, Assets(), workingDirectory, null, default));
        connection = Connection();
        var hostConnection = connection.ConnectionInfo;
        hostConnection.Endpoint = "";
        connection.ConnectionInfo = hostConnection;
        Assert.Throws<ArgumentException>(() => subject.Build(
            dotnet, Assets(), workingDirectory, null, connection));
    }

    private static PortableTestHostAssets Assets()
    {
        var root = Path.GetFullPath("testhost");
        return new PortableTestHostAssets(
            Path.Combine(root, "testhost.dll"),
            Path.Combine(root, "testhost.deps.json"),
            Path.Combine(root, "testhost.runtimeconfig.json"),
            Path.Combine(root, "packages"));
    }

    internal static TestRunnerConnectionInfo Connection() => new()
    {
        Port = 1234,
        RunnerProcessId = 5678,
        ConnectionInfo = new TestHostConnectionInfo
        {
            Endpoint = "127.0.0.1:1234",
            Role = ConnectionRole.Client,
            Transport = Transport.Sockets,
        },
    };
}
