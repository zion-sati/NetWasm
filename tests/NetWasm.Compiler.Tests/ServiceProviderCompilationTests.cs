// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ServiceProviderCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesStandardServiceProviderDispatch(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;

            namespace ServiceProviderFixture;

            public sealed class Provider : IServiceProvider
            {
                object? IServiceProvider.GetService(Type serviceType) =>
                    serviceType == typeof(int) ? 37 : null;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IServiceProvider provider = new Provider();
                    var service = provider.GetService(typeof(int));
                    var missing = provider.GetService(typeof(string));

                    return service is int value && missing is null
                        ? value + input
                        : -1;
                }
            }
            """;
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        var observed = executor.Execute(new CompilationScenario(
            optimize ? "ServiceProviderOptimized" : "ServiceProviderDebug",
            source,
            "ServiceProviderFixture.EntryPoint",
            optimize,
            target,
            5,
            []));

        Assert.Equal(42, observed);
    }
}
