// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A project with localized resources produces one satellite assembly per culture, each named App.resources and told
// apart by its culture (cs-CZ/App.resources.dll, de-DE/App.resources.dll), and a reference to the project brings them
// all. They are no program assemblies (NetWasm's globalization is invariant, so no culture loads them), so the compile
// leaves them out rather than stopping with NW1005 on their shared name (irihitech's Ursa demo has six).
public sealed class SatelliteAssemblyCompilationTests
{
    private const string Program = """
        public static class EntryPoint
        {
            public static int Run(int input) => input + 1;
        }
        """;

    [Fact]
    public void SatellitesOfDifferentCulturesAreLeftOut()
    {
        using var assets = TestAssets.Create();
        var satellites = new List<string>();
        foreach (var culture in new[] { "cs-CZ", "de-DE" })
        {
            var built = assets.CompileSource("App.resources", $"[assembly: System.Reflection.AssemblyCulture(\"{culture}\")]");
            var directory = Directory.CreateDirectory(Path.Combine(assets.Directory, culture)).FullName;
            var satellite = Path.Combine(directory, "App.resources.dll");
            File.Move(built, satellite);
            satellites.Add(satellite);
        }
        var assembly = assets.CompileSource("App", Program);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib, .. satellites], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.None));

        Assert.Equal(42, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32,
            result.StaticDataEnd));
    }
}
