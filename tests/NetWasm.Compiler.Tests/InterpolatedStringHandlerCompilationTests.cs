using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Interpolated strings format each hole into one pooled buffer: alignment,
// format strings, custom formatters, enums, nullable values, span holes, a
// caller's initial buffer and values that need the buffer to grow.
public sealed class InterpolatedStringHandlerCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void InterpolatedHolesFormatAsOnDesktop(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"InterpolationHoles_{optimize}_{target}",
            Source,
            "InterpolationHoles.EntryPoint",
            optimize,
            target,
            7,
            []));

        // As desktop .NET computes it, with ICU or invariant globalization.
        Assert.Equal(-1129684305, result);
    }

    private const string Source = """
        using System;
        using System.Globalization;

        namespace InterpolationHoles;

        public enum Shade { Dark = 1, Light = 4 }

        public struct Wide : ISpanFormattable
        {
            public int Count;
            public string ToString(string? format, IFormatProvider? provider) => new('w', Count);
            public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
            {
                if (destination.Length < Count)
                {
                    // Only this copy changes: the handler formats the value again.
                    Count = 1;
                    charsWritten = 0;
                    return false;
                }
                destination.Slice(0, Count).Fill(format.Length == 0 ? 'w' : format[0]);
                charsWritten = Count;
                return true;
            }
        }

        public sealed class Celsius(double degrees) : IFormattable
        {
            public string ToString(string? format, IFormatProvider? provider) => degrees.ToString(format ?? "F1", provider) + "C";
        }

        public sealed class Plain(int id)
        {
            public override string ToString() => "plain" + id;
        }

        public sealed class Bracketing : IFormatProvider, ICustomFormatter
        {
            public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
            public string Format(string? format, object? arg, IFormatProvider? formatProvider) =>
                arg is int number ? $"<{number}:{format}>" : arg?.ToString() ?? "null";
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var text = Describe(input);
                var hash = text.Length;
                foreach (var character in text) hash = hash * 31 + character;
                return hash;
            }

            public static string Describe(int input)
            {
                var frame = (char)('A' + input);
                var wide = new Wide { Count = input + 250 };
                int? some = input * 3;
                int? none = null;
                string? missing = null;
                object boxed = input + 0.5;
                var span = "span-text".AsSpan(0, 4 + input % 3);
                Span<char> initial = stackalloc char[8];
                var parts = new[]
                {
                    $"{frame}{input}|{-input,5}|{input,-4}|{input:X4}|{input * 1000.25:F2}|{input,8:N1}",
                    $"[{wide}] [{wide:z}] {wide.Count}",
                    $"{Shade.Light}|{(Shade)input}|{Shade.Dark:D}|{Shade.Light,7}",
                    $"{some}|{none}|{missing}|{boxed}|{new Celsius(input + 20.3)}|{new Celsius(-3):F0}|{new Plain(input)}",
                    $"{span}|{span,9}|{span,-9}|{span.Slice(1)}",
                    string.Create(new Bracketing(), $"{input}{input:x}{"s"}{none}"),
                    string.Create(CultureInfo.InvariantCulture, initial, $"{input,20}{frame,3}{new string('x', 40 + input)}"),
                    $"{new string('-', 300)}{input}",
                };
                return string.Join("/", parts);
            }
        }
        """;
}
