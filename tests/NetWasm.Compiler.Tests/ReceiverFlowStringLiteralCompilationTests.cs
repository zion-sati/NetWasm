// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A string literal is a System.String whether or not the closure has allocated a string yet: the closure's flow starts
// before its first drain, so a literal must not depend on the allocated types. A virtual call on a literal or a Box
// gets exactly those two receivers.
public sealed class ReceiverFlowStringLiteralCompilationTests
{
    private const string Source = """
        public sealed class Box { public override string ToString() => "box"; }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = input == 0 ? "text" : new Box();
                return value.ToString()!.Length;
            }
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void AStringLiteralReachesACallAsAString(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowStringLiteralFixture", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));

        var site = Assert.Single(result.Program.DispatchCallSites.Values,
            site => site.Caller.Contains("EntryPoint", StringComparison.Ordinal) &&
                site.Declaration.Definition.Name == "ToString");
        Assert.True(site.PreciseTargets);
        Assert.Equal(["Box", "primitive:string"],
            site.Targets.Select(target => target.ReceiverType.CanonicalName.Contains("Box", StringComparison.Ordinal)
                ? "Box" : target.ReceiverType.CanonicalName).Order(StringComparer.Ordinal));
        Assert.Equal(4, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, WasmTarget.Wasm32,
            result.StaticDataEnd));
        Assert.Equal(3, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 1, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }
}
