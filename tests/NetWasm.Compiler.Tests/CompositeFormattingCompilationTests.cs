// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class CompositeFormattingCompilationTests
{
    [Fact]
    public void CompilerExecutesInvariantCompositeFormattingAndEscapedBraces()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CompositeFormattingFixture",
            """
            using System;

            namespace CompositeFormattingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var text = string.Format("{{{0,5:G}}}|{1,-4}|{2}", input, 1.25m, null);
                    return text == "{   41}|1.25|" ? 1 : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CompositeFormattingFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(1, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPreservesCompositeFormattingAndMalformedInputFailures()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CompositeFormattingFailuresFixture",
            """
            using System;

            namespace CompositeFormattingFailuresFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var failures = 0;
                    try { _ = string.Format(null!, input); } catch (ArgumentNullException) { failures++; }
                    try { _ = string.Format("{1}", input); } catch (FormatException) { failures++; }
                    var formatted = string.Format("{0:X}", input);
                    try { _ = string.Format("broken }", input); } catch (FormatException) { failures++; }
                    return failures == 3 && formatted == "0" ? 4 : failures;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CompositeFormattingFailuresFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(4, ExecuteWithNode(result.ApplicationModule, assets.Directory, 0));
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesStandardFixedPointFloatingFormattingAcrossCilProfilesAndTargets(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source =
            """
            using System.Globalization;

            namespace FixedPointFloatingFormattingFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    return 0.0.ToString("F3", CultureInfo.InvariantCulture) == "0.000"
                        && (-0.0).ToString("F3", CultureInfo.InvariantCulture) == "-0.000"
                        && 0.0006.ToString("F3", CultureInfo.InvariantCulture) == "0.001"
                        && 0.0004.ToString("F3", CultureInfo.InvariantCulture) == "0.000"
                        && 1.5f.ToString("F3", CultureInfo.InvariantCulture) == "1.500"
                        && double.NaN.ToString("F3", CultureInfo.InvariantCulture) == "NaN"
                        && double.PositiveInfinity.ToString("F3", CultureInfo.InvariantCulture) == "Infinity"
                        ? 1
                        : 0;
                }
            }
            """;
        var assembly = optimize
            ? assets.CompileOptimizedSource("FixedPointFloatingFormattingReleaseFixture", source)
            : assets.CompileSource("FixedPointFloatingFormattingDebugFixture", source);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "FixedPointFloatingFormattingFixture.EntryPoint",
            "Run",
            [],
            Target: target));

        Assert.Equal(1, ExecuteWithStandardWasiNode(
            result.ApplicationModule,
            assets.Directory,
            0,
            target,
            result.StaticDataEnd));
    }
}
