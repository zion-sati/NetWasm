using NetWasm.Testing.VSTest.Configuration;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class InvokingDotnetResolver : IInvokingDotnetResolver
{
    private readonly Func<string?> _processPath;
    private readonly Func<string, bool> _fileExists;

    internal InvokingDotnetResolver()
        : this(() => Environment.ProcessPath, File.Exists)
    {
    }

    internal InvokingDotnetResolver(Func<string?> processPath, Func<string, bool> fileExists)
    {
        _processPath = processPath ?? throw new ArgumentNullException(nameof(processPath));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    public string Resolve(NetWasmRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var path = configuration.DotnetHostPath ?? _processPath();
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\0', StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetFullPath(path), path, PathComparison)
            || !IsDotnetFileName(Path.GetFileName(path))
            || !_fileExists(path))
        {
            throw new InvalidOperationException(
                "VSTest did not supply a valid invoking dotnet host for the NetWasm testhost.");
        }

        return path;
    }

    private static bool IsDotnetFileName(string fileName) =>
        string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
