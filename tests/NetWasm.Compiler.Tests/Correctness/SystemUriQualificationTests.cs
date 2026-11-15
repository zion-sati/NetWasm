// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SystemUriQualificationTests
{
    [Fact]
    public void UriCoreContractsMatchDesktopOracleOnWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        runner.Run(CorrectnessTestAssets.CreateUriFixture());
    }

    [Fact]
    public void UriU01BParsingAndResolutionMatchDesktopOnWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        runner.Run(CorrectnessTestAssets.CreateUriU01BFixture());
    }

    [Fact]
    public void UriU01CEscapingAndComponentsMatchDesktopOnWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        runner.Run(CorrectnessTestAssets.CreateUriU01CFixture());
    }

    [Fact]
    public void UriU02AHostsParsersAndBuildersMatchDesktopOnWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        runner.Run(CorrectnessTestAssets.CreateUriU02AFixture());
    }

    [Fact]
    public void UriU02BIdnIriAndApiAuditMatchDesktopOnWasm32AndWasm64()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();

        runner.Run(CorrectnessTestAssets.CreateUriU02BFixture());
    }
}
