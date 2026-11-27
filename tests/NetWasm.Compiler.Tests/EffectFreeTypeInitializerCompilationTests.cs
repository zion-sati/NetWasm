// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;
using static NetWasm.Compiler.Tests.CompilerTestSupport;

namespace NetWasm.Compiler.Tests;

// A type initializer that only stores default values into its own type's statics (Roslyn's for `= 0`, `= null` and
// `= default`) changes nothing, so a static access checks no initialization; one that stores anything else, -0.0
// included, still runs before the access, as on desktop .NET.
public sealed class EffectFreeTypeInitializerCompilationTests
{
    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void DefaultStoresNeedNoInitializationCheck(FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "EffectFreeInitializerFixture",
            """
            using System.Runtime.CompilerServices;

            public struct Pair { public int A; public long B; }

            public static class Defaults
            {
                public static int Count = 0;
                public static long Total = 0;
                public static double Ratio = 0.0;
                public static object Item = null;
                public static Pair Both = default;
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Read() => Count + (int)Total + (int)Ratio + (Item is null ? 1 : 0) + Both.A;
            }

            public static class Generic<T>
            {
                public static T Value = default!;
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static bool IsDefault() => Value is null || Value.Equals(default(T));
            }

            public static class One
            {
                public static int Count = 1;
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Read() => Count;
            }

            public static class NegativeZero
            {
                public static double Value = -0.0;
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Read() => double.IsNegative(Value) ? 1 : 0;
            }

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    Defaults.Read() * 1000 + (Generic<string>.IsDefault() ? 100 : 0) + (Generic<int>.IsDefault() ? 10 : 0) +
                    One.Read() + NegativeZero.Read() + input;
            }
            """);
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization));
        Assert.Equal(1000 + 100 + 10 + 1 + 1 + 3, ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 3, WasmTarget.Wasm32, result.StaticDataEnd));

        var functions = Functions(assets, result.ApplicationModule);
        foreach (var name in new[] { "$Defaults::Read", "$\"[EffectFreeInitializerFixture]Generic`1<primitive:string>::",
            "$\"[EffectFreeInitializerFixture]Generic`1<primitive:i4>::" })
        {
            Assert.DoesNotContain("static.initialize", Function(functions, name), StringComparison.Ordinal);
        }
        Assert.DoesNotContain(functions.Keys, key => key.StartsWith("$Defaults::.cctor", StringComparison.Ordinal));
        foreach (var name in new[] { "$One::Read", "$NegativeZero::Read" })
        {
            Assert.Contains("static.initialize", Function(functions, name), StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, string> Functions(TestAssets assets, byte[] application)
    {
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, application);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return Regex.Split(printed.StandardOutput, @"\n\s*\(func ")
            .Skip(1)
            .GroupBy(static function => function[..function.IndexOfAny([' ', '\n'])], StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
    }

    private static string Function(Dictionary<string, string> functions, string name) =>
        functions.FirstOrDefault(pair => pair.Key.StartsWith(name, StringComparison.Ordinal)).Value ??
        throw new Xunit.Sdk.XunitException($"no function {name} among {string.Join(", ", functions.Keys.Where(key => !key.Contains("System.")).Take(40))}");
}
