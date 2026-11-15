// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class SwitchExpressionCompilationTests
{
    public static TheoryData<bool, WasmTarget> Configurations => new()
    {
        { false, WasmTarget.Wasm32 },
        { false, WasmTarget.Wasm64 },
        { true, WasmTarget.Wasm32 },
        { true, WasmTarget.Wasm64 },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    public void CompilerExecutesDenseAndSparseSwitchExpressionMethodArms(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string librarySource = """
            namespace SwitchExpressionLibrary;

            public static class SwitchArmTargets
            {
                public static int First() => 11;

                public static int Second() => 22;

                public static int Third() => 33;
                public static int Fourth() => 44;
                public static int Fifth() => 55;
                public static int Sixth() => 66;
                public static int Seventh() => 77;
                public static int Eighth() => 88;
                public static int Ninth() => 99;
                public static int Tenth() => 110;
                public static int Combined() => 32767;
            }
            """;
        const string applicationSource = """
            using SwitchExpressionLibrary;

            namespace SwitchExpressionApplication;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    1 => SwitchArmTargets.First(),
                    2 => SwitchArmTargets.Second(),
                    3 => SwitchArmTargets.Third(),
                    4 => SwitchArmTargets.Fourth(),
                    5 => SwitchArmTargets.Fifth(),
                    6 => SwitchArmTargets.Sixth(),
                    7 => SwitchArmTargets.Seventh(),
                    8 => SwitchArmTargets.Eighth(),
                    9 => SwitchArmTargets.Ninth(),
                    10 => SwitchArmTargets.Tenth(),
                    41 => SwitchArmTargets.Combined(),
                    _ => 0
                };
            }
            """;
        var library = optimize
            ? assets.CompileOptimizedSource("GenericReferenceComparisonLibrary", librarySource)
            : assets.CompileSource("GenericReferenceComparisonLibrary", librarySource);
        var assembly = optimize
            ? assets.CompileOptimizedSource(
                "GenericReferenceComparisonApplication",
                applicationSource,
                library)
            : assets.CompileSource(
                "GenericReferenceComparisonApplication",
                applicationSource,
                library);
        var compilation = NetWasmCompiler.Compile(
            new CompilerOptions(
                assembly,
                [assets.CoreLib, library],
                "SwitchExpressionApplication.EntryPoint",
                "Run",
                [],
                target));
        foreach (var (input, expected) in new[]
                 {
                     (1, 11), (2, 22), (3, 33), (4, 44), (5, 55),
                     (6, 66), (7, 77), (8, 88), (9, 99), (10, 110),
                     (41, 32767), (0, 0)
                 })
        {
            var result = CompilerTestSupport.ExecuteWithStandardWasiNode(compilation.ApplicationModule, assets.Directory, input, target, compilation.StaticDataEnd);
            Assert.Equal(expected, result);
        }
    }
}
