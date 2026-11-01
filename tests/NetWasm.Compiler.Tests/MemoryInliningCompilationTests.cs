using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Memory<T> and ReadOnlyMemory<T> are upstream's: their constructors, Slice and
// Span are AggressiveInlining, and Span tells an array from a MemoryManager<T> by
// RuntimeHelpers.ObjectHasComponentSize instead of casting. NetWasm's own versions
// threw inline and were too large to inline, so every slice called two methods,
// and Span cast its owner to MemoryManager<T> and then to T[].
public sealed partial class MemoryInliningCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static readonly byte[] Bytes = new byte[64];
            private static readonly string[] Strings = new string[64];

            public static int Run(int input)
            {
                Bytes[input + 10] = (byte)(input + 3);
                Strings[input + 20] = "xy";
                return SliceBytes(input) + SliceStrings(input) * 100 + StringMemory(input) * 1000;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SliceBytes(int input) => new Memory<byte>(Bytes).Slice(input).Span[10];

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SliceStrings(int input) =>
                new ReadOnlyMemory<string>(Strings).Slice(input, 30).Span[20].Length;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StringMemory(int input) => "hello".AsMemory().Slice(input, 2).Span[1];
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    public void SlicesAndSpansOfMemoryInline(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("MemoryInlining", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        var module = Path.Combine(assets.Directory, "application.wasm");
        File.WriteAllBytes(module, result.ApplicationModule);
        using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
        var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
        Assert.True(printed.ExitCode == 0, printed.StandardError);
        foreach (var method in new[] { "SliceBytes", "SliceStrings", "StringMemory" })
        {
            var body = Function().Matches(printed.StandardOutput)
                .Single(function => function.Groups["name"].Value == $"EntryPoint::{method}").Value;
            Assert.DoesNotMatch(MemoryCall(), body);
        }

        // As desktop .NET computes them: the byte written at input + 10, the
        // string's length 2, and "hello"[input + 1].
        foreach (var (input, expected) in new[] { (0, 3 + 200 + 101000), (1, 4 + 200 + 108000), (2, 5 + 200 + 108000) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$(?<name>\S+)[\s\S]*?(?=\n  \(func |\n  \(export |\z)")]
    private static partial Regex Function();

    [GeneratedRegex(@"call \$""?\[NetWasm\.CoreLib\]System\.(ReadOnly)?Memory`1")]
    private static partial Regex MemoryCall();
}
