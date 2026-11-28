// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// A library caches a feature switch in a static readonly field and branches on the field, as Devolutions'
// MarkupExtensionHelpers caches RuntimeFeature.IsDynamicCodeSupported to choose between a dynamic parse and a reflection
// one. The switch is false on NetWasm, and the field holds false wherever it is read, before its store as after it: every
// store of it stores false. So the read folds to false and the dynamic path, here DynamicPath, is no part of the program,
// as Native AOT's preinitialization leaves it out; otherwise its references (Microsoft.CSharp's binder in the library's
// case) stop the build. The class's earlier initializer, which calls a method, puts the store after other code, so the
// constructor still runs where .NET's would.
public sealed partial class CachedFeatureSwitchCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace CachedFeatureSwitch;

        public static class Parser
        {
            private static readonly string Prefix = MakePrefix();
            private static readonly bool IsDynamicCodeSupported = RuntimeFeature.IsDynamicCodeSupported;
            private static readonly bool AlsoUnsupported = RuntimeFeature.IsDynamicCodeCompiled;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string MakePrefix() => "p" + Environment.ProcessorCount.ToString();

            public static int Parse(string text) => IsDynamicCodeSupported ? DynamicPath(text) : StaticPath(text);

            public static int Compiled(string text) => AlsoUnsupported ? DynamicPath(text) + 1 : Prefix.Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int DynamicPath(string text) => text.GetHashCode() * 7;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StaticPath(string text) => text.Length;
        }

        public static class EntryPoint
        {
            public static int Run(int input) =>
                Parser.Parse(new string('x', input)) + (Parser.Compiled("abc") > 0 ? 0 : 100);
        }
        """;

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.O3, WasmTarget.Wasm32)]
    [InlineData(FinalWasmOptimization.Oz, WasmTarget.Wasm64)]
    public void AFeatureSwitchCachedInAStaticReadonlyFieldFolds(FinalWasmOptimization optimization, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource($"CachedFeatureSwitch_{optimization}_{target}", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib],
            "CachedFeatureSwitch.EntryPoint", "Run", [], Target: target, Optimization: optimization));
        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);

        var functions = FunctionNames().Matches(printed.StandardOutput).Select(static name => name.Value).ToArray();
        Assert.DoesNotContain(functions, static name => name.Contains("DynamicPath", StringComparison.Ordinal));
        Assert.Equal(5, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 5, target, result.StaticDataEnd));
    }

    [GeneratedRegex(@"\(func \$\S+")]
    private static partial Regex FunctionNames();
}
