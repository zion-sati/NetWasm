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

// An interface call with many targets calls one shared dispatch function per declaration (SharedVirtualDispatch):
// 40 implementations, classes and structs, through an interface whose methods take arguments and return a scalar or
// a struct, give each implementation's result; with few implementations the call dispatches in place.
public sealed partial class SharedVirtualDispatchCompilationTests
{
    private const int Implementations = 40;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O0)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O0)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void ManyTargetInterfaceCallsShareOneDispatchFunctionPerDeclaration(WasmTarget target, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SharedVirtualDispatchMany", Source(Implementations));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target, Optimization: optimization));

        // One each for Area, Bounds and Name (named by metadata token).
        var shared = SharedDispatchFunctions(assets, result.ApplicationModule);
        Assert.Equal(3, shared.Length);
        Assert.All(shared, function => Assert.StartsWith("IShape::", function, StringComparison.Ordinal));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, target, result.StaticDataEnd));
    }

    [Fact]
    public void FewTargetInterfaceCallsDispatchInPlace()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SharedVirtualDispatchFew", Source(4));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: WasmTarget.Wasm32));

        Assert.Empty(SharedDispatchFunctions(assets, result.ApplicationModule));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(
            result.ApplicationModule, assets.Directory, 41, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    // Implementation i: a class for even i, a struct for odd i, so value-type receivers dispatch to their payload. T1
    // has a static constructor and is created without its constructor running, so its first interface call runs it.
    private static string Source(int count)
    {
        var source = new StringBuilder("""
            public struct Pair { public long A; public int B; }
            public interface IShape { int Area(int scale); Pair Bounds(int x); string Name(); }

            """);
        var area = 0;
        long boundsA = 0;
        var boundsB = 0;
        var nameLength = 0;
        for (var i = 0; i < count; i++)
        {
            var kind = i % 2 == 0 ? "sealed class" : "struct";
            var field = i % 2 == 0 ? "public int V = " + i + ";" : "public int V;";
            var precise = i == 1;
            if (precise)
            {
                field += " static int s_bias; static T1() { s_bias = 1; }";
            }
            source.AppendLine(CultureInfo.InvariantCulture, $$"""
                public {{kind}} T{{i}} : IShape
                {
                    {{field}}
                    public T{{i}}(int v) { V = v; }
                    public int Area(int scale) => {{i}} * scale + {{(precise ? "s_bias" : "1")}};
                    public Pair Bounds(int x) => new Pair { A = {{i}}L * 1000 + x, B = V + {{i}} };
                    public string Name() => "t{{i}}";
                }
                """);
            area += i * 2 + 1;
            boundsA += i * 1000L + 3;
            boundsB += (precise ? 0 : i) + i;
            nameLength += ("t" + i).Length;
        }
        var shapes = string.Join(", ", Enumerable.Range(0, count).Select(i => i == 1 ? "new T1()" : $"new T{i}({i})"));
        source.AppendLine(CultureInfo.InvariantCulture, $$"""
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    IShape[] shapes = [{{shapes}}];
                    var area = 0;
                    long boundsA = 0;
                    var boundsB = 0;
                    var names = 0;
                    foreach (var shape in shapes)
                    {
                        area += shape.Area(2);
                        var bounds = shape.Bounds(3);
                        boundsA += bounds.A;
                        boundsB += bounds.B;
                        names += shape.Name().Length;
                    }
                    return area == {{area}} && boundsA == {{boundsA}}L && boundsB == {{boundsB}} && names == {{nameLength}}
                        ? input + 1 : 0;
                }
            }
            """);
        return source.ToString();
    }

    // The declarations whose shared dispatch functions the module defines, as Type::Method.
    private static string[] SharedDispatchFunctions(TestAssets assets, byte[] module)
    {
        var path = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(path, module);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", path);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        return [.. SharedDispatchName().Matches(printed.StandardOutput)
            .Select(static match => $"{match.Groups["type"].Value}::{match.Groups["method"].Value}")
            .Distinct()
            .Order(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"\(func \$""virtual\.dispatch<(?:\[[^\]]*\])?(?<type>[A-Za-z0-9_]+)::(?<method>[A-Za-z0-9_]+)")]
    private static partial Regex SharedDispatchName();
}
