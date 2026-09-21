using NetWasm.Testing.VSTest.Configuration;

namespace NetWasm.Testing.VSTest.Tests.Configuration;

public sealed class NetWasmRunSettingsReaderTests
{
    [Fact]
    public void ReadsOnlyTheCanonicalNetWasmRunConfiguration()
    {
        const string settings = """
            <RunSettings>
              <RunConfiguration>
                <TargetFrameworkVersion>NetWasm,Version=v0.1</TargetFrameworkVersion>
                <DotNetHostPath>/tools/dotnet</DotNetHostPath>
              </RunConfiguration>
            </RunSettings>
            """;

        var accepted = new NetWasmRunSettingsReader().TryRead(settings, out var configuration);

        Assert.True(accepted);
        Assert.Equal("NetWasm,Version=v0.1", configuration!.TargetFrameworkMoniker);
        Assert.Equal("/tools/dotnet", configuration.DotnetHostPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<RunSettings>")]
    [InlineData("<RunSettings />")]
    [InlineData("<RunSettings><RunConfiguration /></RunSettings>")]
    [InlineData("<RunSettings><RunConfiguration><TargetFrameworkVersion>netwasm0.1</TargetFrameworkVersion></RunConfiguration></RunSettings>")]
    [InlineData("<RunSettings><RunConfiguration><TargetFrameworkVersion>NetWasm,Version=v0.2</TargetFrameworkVersion></RunConfiguration></RunSettings>")]
    [InlineData("<RunSettings><RunConfiguration><TargetFrameworkVersion>.NETCoreApp,Version=v10.0</TargetFrameworkVersion></RunConfiguration></RunSettings>")]
    public void DeclinesMalformedAliasedOrNonNetWasmSettings(string? settings)
    {
        Assert.False(new NetWasmRunSettingsReader().TryRead(settings, out var configuration));
        Assert.Null(configuration);
    }

    [Fact]
    public void AcceptsNamespacedSettingsWithoutWeakeningElementIdentity()
    {
        const string settings = """
            <RunSettings xmlns="urn:vstest">
              <RunConfiguration>
                <TargetFrameworkVersion>NetWasm,Version=v0.1</TargetFrameworkVersion>
              </RunConfiguration>
            </RunSettings>
            """;

        Assert.True(new NetWasmRunSettingsReader().TryRead(settings, out var configuration));
        Assert.Null(configuration!.DotnetHostPath);
    }
}
