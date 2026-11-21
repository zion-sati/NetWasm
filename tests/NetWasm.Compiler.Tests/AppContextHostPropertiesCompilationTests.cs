// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// AppContext answers the two properties Mono's WebAssembly hosts give every program: the base directory is the
// sandbox root, so code that walks up from AppContext.BaseDirectory (to find test data, say) runs as it does on
// Mono instead of failing on an empty path, and the runtime identifier is RuntimeInformation's. A project's
// runtime property of the same name still wins.
public sealed class AppContextHostPropertiesCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void TheBaseDirectoryIsTheSandboxRootAndTheRuntimeIdentifierTheTargets(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("HostPropertiesFixture", Source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "HostPropertiesFixture.EntryPoint", "Run", [], Target: target));

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void AProjectsBaseDirectoryPropertyWins()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("HostPropertiesFixture", Source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "HostPropertiesFixture.EntryPoint", "RunConfigured", []) with
        {
            RuntimeProperties = [("APP_CONTEXT_BASE_DIRECTORY", "/app/")],
        });

        Assert.Equal(42, ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.IO;
        using System.Runtime.InteropServices;
        namespace HostPropertiesFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (AppContext.BaseDirectory != "/") return 1;
                if (AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY") as string != "/") return 2;
                if (AppContext.GetData("RUNTIME_IDENTIFIER") as string != RuntimeInformation.RuntimeIdentifier) return 3;
                if (AppContext.GetData("Example.Missing") is not null) return 4;
                // The walk test data lookups make: the root's parent ends it.
                if (Path.GetDirectoryName(AppContext.BaseDirectory) is not null) return 5;
                return input + 1;
            }

            public static int RunConfigured(int input) =>
                AppContext.BaseDirectory == "/app/" ? input + 1 : 1;
        }
        """;
}
