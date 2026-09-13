using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class TestHostStartInfoBuilder : ITestHostStartInfoBuilder
{
    public TestProcessStartInfo Build(
        string dotnetPath,
        PortableTestHostAssets assets,
        string workingDirectory,
        IDictionary<string, string?>? environmentVariables,
        TestRunnerConnectionInfo connectionInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotnetPath);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var hostConnection = connectionInfo.ConnectionInfo;
        ArgumentException.ThrowIfNullOrWhiteSpace(hostConnection.Endpoint);
        var arguments = new List<string>
        {
            "exec",
            "--runtimeconfig",
            assets.RuntimeConfigurationPath,
            "--depsfile",
            assets.DependencyManifestPath,
            "--additionalprobingpath",
            assets.PackageProbingPath,
            assets.TestHostPath,
            "--port",
            connectionInfo.Port.ToString(CultureInfo.InvariantCulture),
            "--endpoint",
            hostConnection.Endpoint,
            "--role",
            hostConnection.Role == ConnectionRole.Client ? "client" : "host",
            "--parentprocessid",
            connectionInfo.RunnerProcessId.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(connectionInfo.LogFile))
        {
            arguments.Add("--diag");
            arguments.Add(connectionInfo.LogFile);
            arguments.Add("--tracelevel");
            arguments.Add(connectionInfo.TraceLevel.ToString(CultureInfo.InvariantCulture));
        }

        return new TestProcessStartInfo
        {
            FileName = dotnetPath,
            Arguments = string.Join(' ', arguments.Select(Quote)),
            WorkingDirectory = workingDirectory,
            EnvironmentVariables = environmentVariables is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?>(environmentVariables, StringComparer.Ordinal),
        };
    }

    private static string Quote(string argument)
    {
        var result = new StringBuilder(argument.Length + 2).Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append(character);
                backslashes = 0;
                continue;
            }

            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }

        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
