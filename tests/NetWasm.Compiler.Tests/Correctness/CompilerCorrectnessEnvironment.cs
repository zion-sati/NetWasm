namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record CompilerCorrectnessEnvironment(
    string RepositoryRoot,
    string DotNetPath,
    string SdkVersion,
    string RoslynCompilerPath,
    string CoreLibPath,
    string DesktopReferenceDirectory,
    string NodeRunnerPath,
    string DesktopOracleHostPath,
    string CompilerHostPath,
    TimeSpan ProcessTimeout)
{
    public static CompilerCorrectnessEnvironment Discover(string? startDirectory = null)
    {
        var repositoryRoot = FindRepositoryRoot(startDirectory);
        var dotnetPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("dotnet host path is unavailable");
        var sdkVersion = ReadSdkVersion(repositoryRoot);
        var dotnetRoot = Path.GetDirectoryName(dotnetPath)
            ?? throw new InvalidOperationException("dotnet root is unavailable");
        var compiler = Path.Combine(
            dotnetRoot, "sdk", sdkVersion, "Roslyn", "bincore", "csc.dll");
        var referenceRoot = Path.Combine(
            dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var referenceDirectory = Directory.EnumerateDirectories(referenceRoot)
            .Select(path => new
            {
                Path = path,
                Version = Version.TryParse(Path.GetFileName(path), out var version)
                    ? version
                    : new Version(),
            })
            .Where(item => item.Version.Major == 10)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(item.Path, "ref", "net10.0"))
            .First(Directory.Exists);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory)
            .Parent?.Name
            ?? throw new InvalidOperationException(
                "test build configuration could not be discovered");
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
            Path.Combine(repositoryRoot, "tests", "NetWasm.Testing.CompilerHost",
                "bin", configuration, "net10.0", "NetWasm.Testing.CompilerHost.dll"),
            TimeSpan.FromMinutes(10));
    }

    private static string FindRepositoryRoot(string? startDirectory)
    {
        var current = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("repository root was not found");
    }

    private static string ReadSdkVersion(string repositoryRoot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
        return document.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            ?? throw new InvalidOperationException("global.json does not contain an SDK version");
    }
}
