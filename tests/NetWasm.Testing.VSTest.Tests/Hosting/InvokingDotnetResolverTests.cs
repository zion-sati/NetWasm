using NetWasm.Testing.VSTest.Configuration;
using NetWasm.Testing.VSTest.Hosting;

namespace NetWasm.Testing.VSTest.Tests.Hosting;

public sealed class InvokingDotnetResolverTests
{
    [Fact]
    public void PrefersTheExplicitVSTestDotnetHost()
    {
        var explicitPath = DotnetPath("explicit");
        var processPath = DotnetPath("process");
        var subject = new InvokingDotnetResolver(
            () => processPath,
            path => path == explicitPath);

        var result = subject.Resolve(Configuration(explicitPath));

        Assert.Equal(explicitPath, result);
    }

    [Fact]
    public void UsesOnlyTheCurrentDotnetProcessWhenNoExplicitHostWasSupplied()
    {
        var processPath = DotnetPath("process");
        var subject = new InvokingDotnetResolver(
            () => processPath,
            path => path == processPath);

        var result = subject.Resolve(Configuration(null));

        Assert.Equal(processPath, result);
    }

    [Fact]
    public void DefaultCompositionUsesTheCurrentDotnetHost()
    {
        var processPath = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(processPath));

        var result = new InvokingDotnetResolver().Resolve(Configuration(null));

        Assert.Equal(processPath, result);
    }

    [Fact]
    public void AcceptsTheCanonicalDotnetExeFileNameWithoutPlatformDiscovery()
    {
        var path = Path.GetFullPath(Path.Combine("explicit", "dotnet.exe"));

        var result = new InvokingDotnetResolver(() => null, candidate => candidate == path)
            .Resolve(Configuration(path));

        Assert.Equal(path, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/dotnet")]
    [InlineData("contains\0null/dotnet")]
    [InlineData("/tools/node")]
    [InlineData("/tools/dotnet-other")]
    public void RejectsMissingRelativeOrNonDotnetProcessPaths(string? path)
    {
        var subject = new InvokingDotnetResolver(() => path, _ => true);

        Assert.Throws<InvalidOperationException>(() => subject.Resolve(Configuration(null)));
    }

    [Fact]
    public void RejectsAnAbsentDotnetWithoutSearchingPathOrSdkDirectories()
    {
        var processPath = DotnetPath("absent");
        var probes = new List<string>();
        var subject = new InvokingDotnetResolver(
            () => processPath,
            path =>
            {
                probes.Add(path);
                return false;
            });

        Assert.Throws<InvalidOperationException>(() => subject.Resolve(Configuration(null)));
        Assert.Equal([processPath], probes);
    }

    [Fact]
    public void RejectsANonCanonicalDotnetPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "folder", "..", "dotnet");
        var subject = new InvokingDotnetResolver(() => path, _ => true);

        Assert.Throws<InvalidOperationException>(() => subject.Resolve(Configuration(null)));
    }

    [Fact]
    public void RejectsMissingDependenciesAndConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => new InvokingDotnetResolver(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => new InvokingDotnetResolver(() => null, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new InvokingDotnetResolver(() => null, _ => true).Resolve(null!));
    }

    private static NetWasmRunConfiguration Configuration(string? dotnetPath) =>
        new(NetWasmRunSettingsReader.SupportedTargetFrameworkMoniker, dotnetPath);

    private static string DotnetPath(string directory) => Path.GetFullPath(
        Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
}
