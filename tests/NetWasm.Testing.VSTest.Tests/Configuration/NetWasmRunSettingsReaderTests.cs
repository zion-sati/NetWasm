// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

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

    // VSTest receives net10.0-netwasm as .NETCoreApp, like a desktop project; the bridge's build names the
    // platform target framework in a NetWasm.TargetFramework test run parameter.
    [Theory]
    [InlineData("net10.0-netwasm")]
    [InlineData("net10.0-netwasm1.0")]
    [InlineData("NET11.0-NETWASM")]
    public void ReadsAPlatformTargetFrameworkRun(string alias)
    {
        var settings = $"""
            <RunSettings>
              <RunConfiguration>
                <TargetFrameworkVersion>.NETCoreApp,Version=v10.0</TargetFrameworkVersion>
              </RunConfiguration>
              <TestRunParameters>
                <Parameter name="NetWasm.TargetFramework" value="{alias}" />
              </TestRunParameters>
            </RunSettings>
            """;

        Assert.True(new NetWasmRunSettingsReader().TryRead(settings, out var configuration));
        Assert.Equal(".NETCoreApp,Version=v10.0", configuration!.TargetFrameworkMoniker);
        Assert.Null(configuration.DotnetHostPath);
    }

    [Theory]
    // A desktop alias, an empty alias, a framework that is not .NETCoreApp, and netwasm0.1's own framework. (A
    // parameter named twice is VSTest's to resolve: its parser keeps the last value.)
    [InlineData(".NETCoreApp,Version=v10.0", "<Parameter name=\"NetWasm.TargetFramework\" value=\"net10.0\" />")]
    [InlineData(".NETCoreApp,Version=v10.0", "<Parameter name=\"NetWasm.TargetFramework\" value=\"\" />")]
    [InlineData(".NETFramework,Version=v4.7.2", "<Parameter name=\"NetWasm.TargetFramework\" value=\"net10.0-netwasm\" />")]
    [InlineData("NetWasm,Version=v0.1", "<Parameter name=\"NetWasm.TargetFramework\" value=\"net10.0-netwasm\" />")]
    public void DeclinesARunThatIsNotAPlatformTargetFrameworkRun(string framework, string parameters)
    {
        var settings = $"""
            <RunSettings>
              <RunConfiguration>
                <TargetFrameworkVersion>{framework}</TargetFrameworkVersion>
              </RunConfiguration>
              <TestRunParameters>{parameters}</TestRunParameters>
            </RunSettings>
            """;

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
