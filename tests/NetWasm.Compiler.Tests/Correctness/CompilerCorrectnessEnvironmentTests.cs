namespace NetWasm.Compiler.Tests.Correctness;

using NetWasm.TestInfrastructure;

public sealed class CompilerCorrectnessEnvironmentTests
{
    [Fact]
    public void ExplicitBuildConfigurationSurvivesRelocatedTestOutput()
    {
        var configuration = CompilerCorrectnessEnvironment.ResolveBuildConfiguration(
            Path.Combine("artifacts", "shard-01", "test-bin", "net10.0"),
            "Release");

        Assert.Equal("Release", configuration);
    }

    [Fact]
    public void BuildConfigurationFallsBackToParentOutputDirectory()
    {
        var configuration = CompilerCorrectnessEnvironment.ResolveBuildConfiguration(
            Path.Combine("bin", "Debug", "net10.0"),
            null);

        Assert.Equal("Debug", configuration);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../Release")]
    [InlineData("..")]
    public void InvalidExplicitBuildConfigurationIsRejected(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            CompilerCorrectnessEnvironment.ResolveBuildConfiguration(
                Path.Combine("artifacts", "shard-01", "test-bin", "net10.0"),
                value));
    }

    [Fact]
    public void ExplicitRepositoryRootSurvivesRelocatedTestOutput()
    {
        var expected = TestAssets.FindRepositoryRoot();
        var repositoryRoot = TestAssets.ResolveRepositoryRoot(
            Path.Combine(Path.GetTempPath(), "shard-01", "test-bin"),
            expected);

        Assert.Equal(expected, repositoryRoot);
    }

    [Fact]
    public void InvalidExplicitRepositoryRootIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestAssets.ResolveRepositoryRoot(AppContext.BaseDirectory, directory));

        Assert.Contains(TestAssets.RepositoryRootEnvironmentVariable, exception.Message);
    }

    [Fact]
    public void DiscoverResolvesPinnedExistingToolchainAndRunnerPaths()
    {
        var environment = CompilerCorrectnessEnvironment.Discover();

        Assert.True(File.Exists(Path.Combine(environment.RepositoryRoot, "global.json")));
        Assert.True(File.Exists(environment.DotNetPath));
        Assert.True(File.Exists(environment.RoslynCompilerPath));
        Assert.True(File.Exists(environment.CoreLibPath));
        Assert.True(Directory.Exists(environment.DesktopReferenceDirectory));
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(environment.DotNetPath)!, "packs", "Microsoft.NETCore.App.Ref",
                environment.DesktopOracleFrameworkVersion, "ref", "net11.0"),
            environment.DesktopReferenceDirectory);
        Assert.True(File.Exists(environment.NodeRunnerPath));
        Assert.Equal("10.0.403", environment.SdkVersion);
        Assert.StartsWith("11.0.", environment.DesktopOracleFrameworkVersion, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(
            Path.GetDirectoryName(environment.DotNetPath)!, "shared", "Microsoft.NETCore.App",
            environment.DesktopOracleFrameworkVersion)));
        Assert.Equal(TimeSpan.FromMinutes(10), environment.ProcessTimeout);
    }
}
