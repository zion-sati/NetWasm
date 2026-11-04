namespace NetWasm.Compiler.Tests.Correctness;

using NetWasm.TestInfrastructure;

internal sealed record CompilerCorrectnessEnvironment(
    string RepositoryRoot,
    string DotNetPath,
    string SdkVersion,
    string RoslynCompilerPath,
    string CoreLibPath,
    string DesktopReferenceDirectory,
    string NodeRunnerPath,
    string DesktopOracleHostPath,
    string DesktopOracleFrameworkVersion,
    string CompilerHostPath,
    TimeSpan ProcessTimeout)
{
    internal const string BuildConfigurationEnvironmentVariable =
        "NETWASM_TEST_BUILD_CONFIGURATION";

    public static CompilerCorrectnessEnvironment Discover(string? startDirectory = null)
    {
        var repositoryRoot = TestAssets.FindRepositoryRoot(startDirectory);
        var dotnetPath = DotNetHost.Executable;
        var sdkVersion = ReadSdkVersion(repositoryRoot);
        var dotnetRoot = Path.GetDirectoryName(dotnetPath)
            ?? throw new InvalidOperationException("dotnet root is unavailable");
        var compiler = Path.Combine(
            dotnetRoot, "sdk", sdkVersion, "Roslyn", "bincore", "csc.dll");
        // The desktop side compiles against the reference pack of the framework it runs on.
        var oracleFrameworkVersion = ReadDesktopOracleFrameworkVersion(repositoryRoot);
        var referenceDirectory = Path.Combine(
            dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", oracleFrameworkVersion, "ref",
            $"net{oracleFrameworkVersion.Split('.')[0]}.0");
        var configuration = ResolveBuildConfiguration(
            AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable(BuildConfigurationEnvironmentVariable));
        return new(
            repositoryRoot,
            dotnetPath,
            sdkVersion,
            compiler,
            Path.Combine(repositoryRoot, "src", "NetWasm.CoreLib", "bin",
                "Release", "net10.0", "NetWasm.CoreLib.dll"),
            referenceDirectory,
            Path.Combine(AppContext.BaseDirectory, "Correctness", "netwasm-oracle.mjs"),
            Path.Combine(repositoryRoot, "tests", "NetWasm.Testing.OracleHost",
                "bin", configuration, "net10.0", "NetWasm.Testing.OracleHost.dll"),
            oracleFrameworkVersion,
            Path.Combine(repositoryRoot, "tests", "NetWasm.Testing.CompilerHost",
                "bin", configuration, "net10.0", "NetWasm.Testing.CompilerHost.dll"),
            TimeSpan.FromMinutes(10));
    }

    internal static string ResolveBuildConfiguration(
        string baseDirectory,
        string? configuredValue)
    {
        if (configuredValue is not null)
        {
            if (string.IsNullOrWhiteSpace(configuredValue) ||
                configuredValue is "." or ".." ||
                configuredValue.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                configuredValue.Contains(Path.DirectorySeparatorChar) ||
                configuredValue.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new InvalidOperationException(
                    $"{BuildConfigurationEnvironmentVariable} must be a build configuration name.");
            }

            return configuredValue;
        }

        return new DirectoryInfo(baseDirectory).Parent?.Name
            ?? throw new InvalidOperationException(
                "test build configuration could not be discovered");
    }

    // Behaviour follows .NET 11, so the oracle host, compiled against .NET 10's references,
    // runs on the pinned .NET 11 runtime the C# 15 toolchain installs.
    private static string ReadDesktopOracleFrameworkVersion(string repositoryRoot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "eng", "csharp15-toolchain.json")));
        return document.RootElement.GetProperty("desktopOracleFrameworkVersion").GetString()
            ?? throw new InvalidOperationException("the desktop oracle framework version is not pinned");
    }

    private static string ReadSdkVersion(string repositoryRoot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        return document.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            ?? throw new InvalidOperationException("global.json does not contain an SDK version");
    }
}
