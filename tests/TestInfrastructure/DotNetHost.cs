namespace NetWasm.TestInfrastructure;

// The dotnet host that runs the tests' child commands, with links resolved so
// its directory is the .NET root. dotnet test sets DOTNET_HOST_PATH; then
// DOTNET_ROOT's host; a test process that dotnet itself runs is that host;
// otherwise the first dotnet on PATH. An xUnit v3 test process is the test
// project's own executable, so its process path is not the host.
public static class DotNetHost
{
    public static string Executable { get; } = Resolve();

    private static string Resolve()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Final(configured);
        }
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, name)))
        {
            return Final(Path.Combine(root, name));
        }
        if (Environment.ProcessPath is { } process &&
            string.Equals(Path.GetFileName(process), name, StringComparison.OrdinalIgnoreCase))
        {
            return Final(process);
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return Final(candidate);
            }
        }
        throw new InvalidOperationException(
            "No dotnet host: DOTNET_HOST_PATH is unset and no dotnet is on PATH.");
    }

    private static string Final(string path) =>
        File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
}
