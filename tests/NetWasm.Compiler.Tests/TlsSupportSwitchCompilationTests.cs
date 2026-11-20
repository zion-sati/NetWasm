// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A program that can reach SslStream imports wasi:tls, which its host must provide. Built with NetWasmTlsSupport false,
// for a host without it, the switch folds at compile time: the program imports no wasi:tls, and SslStream says why it
// is unavailable.
public sealed class TlsSupportSwitchCompilationTests
{
    private const string Switch = "NetWasm.Net.Security.TlsSupport";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSwitchDecidesWhetherTheProgramImportsWasiTls(bool supported)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource($"TlsSupport{supported}", """
            using System;
            using System.IO;
            using System.Net.Security;
            namespace TlsSupportFixture;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var tls = new SslStream(new MemoryStream());
                    var handshake = tls.AuthenticateAsClientAsync("example.com");
                    if (!handshake.IsFaulted) return 1;
                    return handshake.Exception!.InnerException is PlatformNotSupportedException unavailable &&
                        unavailable.Message.Contains("NetWasmTlsSupport") ? input + 1 : 2;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "TlsSupportFixture.EntryPoint",
            "Run", [new("run", "TlsSupportFixture.EntryPoint", "Run")], Target: WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0")
        {
            FeatureSwitches = ImmutableDictionary<string, bool>.Empty.Add(Switch, supported),
        });

        var importsTls = WasmModuleInspection.ReadFunctionImports(result.ApplicationModule)
            .Any(import => import.Module.StartsWith("wasi:tls/", StringComparison.Ordinal));

        Assert.Equal(supported, importsTls);
        if (!supported)
        {
            Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41,
                WasmTarget.Wasm32, result.StaticDataEnd));
        }
    }
}
