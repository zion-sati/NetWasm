// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Array.Copy's primitive conversion is pay-for-use: a program whose copies all take two arrays of one static
// one-dimensional type (List<T> included) carries none of it, and one that may copy between element types, or
// through arrays typed Array, carries it and widens as desktop does.
public sealed partial class ArrayCopyConversionReachTests
{
    private const string OneTypeSource = """
        using System;
        using System.Collections.Generic;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var numbers = new List<int>();
                for (var i = 0; i < 20; i++) numbers.Insert(0, i);
                var source = new[] { 1, 2, 3, 4 };
                var target = new int[6];
                Array.Copy(source, target, 4);
                Array.Copy(source, 1, target, 2, 3);
                Array.Copy(new int[] { 9 }, 0L, target, 5L, 1L);
                var days = new[] { DayOfWeek.Monday, DayOfWeek.Friday };
                var copiedDays = new DayOfWeek[2];
                Array.Copy(days, copiedDays, 2L);
                var names = new object[] { "a", "b" };
                var copiedNames = new object[2];
                Array.Copy(names, copiedNames, 2);
                return numbers[0] == 19 && numbers.Count == 20 && target[0] == 1 && target[2] == 2 && target[4] == 4 &&
                    target[5] == 9 && copiedDays[1] == DayOfWeek.Friday && (string)copiedNames[1] == "b" ? 42 : 1;
            }
        }
        """;

    private const string TwoTypesSource = """
        using System;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var source = new[] { -1, 2 };
                var target = new long[2];
                Array.Copy(source, target, 2);
                return target[0] == -1L && target[1] == 2L ? 42 : 1;
            }
        }
        """;

    private const string ArrayTypedSource = """
        using System;
        public static class EntryPoint
        {
            private static void CopyAll(Array source, Array target) => Array.Copy(source, target, source.Length);

            public static int Run(int input)
            {
                var target = new double[2];
                CopyAll(new[] { 1.5f, -2f }, target);
                return target[0] == 1.5 && target[1] == -2.0 ? 42 : 1;
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CopiesOfOneStaticTypeCarryNoConversion(WasmTarget target) =>
        Assert.False(CompileAndRun("ArrayCopyOneType", OneTypeSource, target));

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CopiesBetweenElementTypesCarryTheConversion(WasmTarget target) =>
        Assert.True(CompileAndRun("ArrayCopyTwoTypes", TwoTypesSource, target));

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void CopiesOfArraysTypedArrayCarryTheConversion(WasmTarget target) =>
        Assert.True(CompileAndRun("ArrayCopyArrayTyped", ArrayTypedSource, target));

    // Runs the program, which returns 42, and answers whether its unoptimized module has the conversion.
    private static bool CompileAndRun(string name, string source, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource(name, source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: FinalWasmOptimization.O0));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", path);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        var conversion = ConversionFunction().Matches(printed.StandardOutput).Select(match => match.Groups["name"].Value)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.True(conversion.Length is 0 or 2, string.Join(", ", conversion));
        return conversion.Length == 2;
    }

    [GeneratedRegex(@"\(func \$""?[^ ""]*(?<name>Array::TryCopyPrimitives|InvokeUtils::PrimitiveWiden)\b")]
    private static partial Regex ConversionFunction();
}
