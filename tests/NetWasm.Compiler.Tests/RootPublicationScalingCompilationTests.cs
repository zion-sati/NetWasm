// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// A method's safepoints publish what changed, not every root at every call. Here a method holds N references, which
// an exception filter captures, live across M calls, plus a string at the bottom of the evaluation stack across all
// of them. Republishing every root at every call made its stores grow as N x M (TUnit's engine spent megabytes so);
// they now grow with N + M, and the larger method stays under a fixed number of stores.
public sealed partial class RootPublicationScalingCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void PublicationStoresGrowWithRootsPlusCallsNotTheirProduct(WasmTarget target)
    {
        var small = Stores(8, 8, target);
        var large = Stores(16, 16, target);

        // Doubling both doubles a sum but quadruples a product.
        Assert.True(large <= small * 5 / 2, $"8 roots x 8 calls: {small} stores; 16 x 16: {large}");
        Assert.True(large <= MaximumLargeStores, $"16 roots x 16 calls: {large} stores");
    }

    // Twice the 30 stores measured on 2026-11-15 (wasm32) with every fix in; republishing the captures at every call
    // alone was 16 x 16 = 256, and as many again into the filter root frame.
    private const int MaximumLargeStores = 60;

    internal static string Source(int roots, int calls)
    {
        var source = new StringBuilder("""
            using System;
            using System.Runtime.CompilerServices;

            namespace RootScalingFixture;

            public static class EntryPoint
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static string Text(int length) => new('x', length + 1);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Call(int value) => value;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static bool Check(Exception failure, string kept) => kept.Length > failure.Message.Length;

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Measure(string text, int value) => text.Length + value;

                public static int Many(int input)
                {

            """);
        for (var root = 0; root < roots; root++) source.AppendLine(CultureInfo.InvariantCulture, $"        var a{root} = Text({root});");
        source.AppendLine("        var total = input;");
        source.AppendLine("        try");
        source.AppendLine("        {");
        for (var call = 0; call < calls; call++) source.AppendLine(CultureInfo.InvariantCulture, $"            total += Call({call});");
        source.AppendLine("        }");
        source.AppendLine(CultureInfo.InvariantCulture, $"        catch (Exception failure) when (Check(failure, {string.Join(" + ", Enumerable.Range(0, roots).Select(root => string.Create(CultureInfo.InvariantCulture, $"a{root}")))}))");
        source.AppendLine("        {");
        source.AppendLine("            total = -1;");
        source.AppendLine("        }");
        source.AppendLine(CultureInfo.InvariantCulture, $"        total += Measure(Text(0), {string.Join(" + ", Enumerable.Range(0, calls).Select(call => string.Create(CultureInfo.InvariantCulture, $"Call({call})")))});");
        source.AppendLine(CultureInfo.InvariantCulture, $"        return total + {string.Join(" + ", Enumerable.Range(0, roots).Select(root => string.Create(CultureInfo.InvariantCulture, $"a{root}.Length")))};");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    public static int Run(int input) => Many(input);");
        source.AppendLine("}");
        return source.ToString();
    }

    // The stores in Many's body, unoptimized: root publication is what grows with roots and calls.
    private static int Stores(int roots, int calls, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("RootScaling", Source(roots, calls));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "RootScalingFixture.EntryPoint", "Run", [], Target: target,
            Optimization: FinalWasmOptimization.O0));
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", path);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var start = ManyStart().Match(printed.StandardOutput);
        Assert.True(start.Success);
        var end = printed.StandardOutput.IndexOf("\n  (func ", start.Index + 1, StringComparison.Ordinal);
        var body = printed.StandardOutput[start.Index..(end < 0 ? printed.StandardOutput.Length : end)];
        return Store().Count(body);
    }

    [GeneratedRegex(@"\(func \$""?[^ ""]*EntryPoint::Many\b", RegexOptions.CultureInvariant)]
    private static partial Regex ManyStart();

    [GeneratedRegex(@"\b(i32|i64|v128)\.store\b")]
    private static partial Regex Store();
}
