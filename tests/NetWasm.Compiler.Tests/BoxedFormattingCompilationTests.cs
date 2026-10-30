using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// Generic formatting tests a value against IFormattable and ISpanFormattable and
// calls TryFormat through a cast, each a box of the value. Once the value type is
// known, the tests are constants and the call reaches the type's own method on a
// copy, so formatting a char or an int allocates nothing, as on .NET's JIT.
public sealed partial class BoxedFormattingCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace BoxedFormatting;

        public interface ICounter
        {
            int Bump();
        }

        public struct Counter(int count) : ICounter
        {
            private int _count = count;

            public int Bump() => ++_count;
        }

        public readonly struct Plain(int id)
        {
            public override string ToString() => "plain" + id;
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Span<char> buffer = stackalloc char[256];
                var written = 0;
                for (var index = 0; index < 3 + input; index++)
                {
                    written += Format((char)('A' + index), buffer.Slice(written));
                    written += Format(index * 1234 - input, buffer.Slice(written));
                    written += Format(new Plain(index), buffer.Slice(written));
                }
                var hash = written;
                foreach (var character in buffer.Slice(0, written)) hash = hash * 31 + character;
                // Every cast boxes its own copy, so each call starts from the value passed in.
                return hash ^ Bump(new Counter(input)) << 24;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Format<T>(T value, Span<char> destination)
            {
                if (value is IFormattable)
                {
                    if (value is ISpanFormattable)
                    {
                        return ((ISpanFormattable)value).TryFormat(destination, out var written, default, null) ? written : -1;
                    }
                    return ((IFormattable)value).ToString(null, null).Length;
                }
                var text = value!.ToString()!;
                text.AsSpan().CopyTo(destination);
                return text.Length;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Bump<T>(T value) where T : ICounter
            {
                ((ICounter)value).Bump();
                ((ICounter)value).Bump();
                return ((ICounter)value).Bump();
            }
        }
        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void FormattingAKnownValueTypeAllocatesNothing(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("BoxedFormatting", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "BoxedFormatting.EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        if (preset == FinalWasmOptimization.O3)
        {
            var module = Path.Combine(assets.Directory, "application.wasm");
            File.WriteAllBytes(module, result.ApplicationModule);
            using var services = new ServiceCollection().AddNetWasmCompiler().BuildServiceProvider();
            var printed = services.GetRequiredService<IWasmTools>().Run("print", module);
            Assert.True(printed.ExitCode == 0, printed.StandardError);
            var functions = Functions().Matches(printed.StandardOutput)
                .Where(static function => SpecializedValueFunction().IsMatch(function.Value))
                .ToArray();
            Assert.True(functions.Length == 3, string.Join('\n', FunctionNames().Matches(printed.StandardOutput)
                .Select(static name => name.Value).Where(static name => name.Contains("BoxedFormatting"))));
            Assert.All(functions, static function => Assert.DoesNotMatch(Allocation(), function.Value));
        }

        // As desktop .NET computes them.
        foreach (var (input, expected) in new[] { (0, 1716002551), (1, 1969744973), (5, -1687645287) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }

    [GeneratedRegex(@"\(func \$\S+.*?(?=\n  \(func |\n  \(type |\z)", RegexOptions.Singleline)]
    private static partial Regex Functions();

    [GeneratedRegex(@"\(func \$\S+")]
    private static partial Regex FunctionNames();

    // Format<char> and Format<int> (token 7) and Bump<Counter> (token 8).
    [GeneratedRegex(@"\A\(func \$""\[BoxedFormatting\]BoxedFormatting\.EntryPoint::(0x06000007<primitive:(char|i4)>|0x06000008<\[BoxedFormatting\]BoxedFormatting\.Counter>)""")]
    private static partial Regex SpecializedValueFunction();

    // The runtime's allocator, by whichever name the collector exports it.
    [GeneratedRegex(@"call \$\S*allocate")]
    private static partial Regex Allocation();
}
