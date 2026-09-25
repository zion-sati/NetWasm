using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BoxedValueCallCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesConsecutiveUnboxedValueArguments(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "BoxedValueCall.Application",
            Source,
            "BoxedValueCall.Application.EntryPoint",
            optimize,
            target,
            -2,
            []));

        Assert.Equal(2006, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerImplementsDefaultValueTypeEqualityForEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "DefaultValueEquality.Application",
            DefaultValueEqualitySource,
            "DefaultValueEquality.Application.EntryPoint",
            optimize,
            target,
            4,
            []));

        Assert.Equal(4095, result);
    }

    private const string Source = """
        namespace BoxedValueCall.Application;

        public readonly record struct Marker(int Value);

        public sealed record Pair(Marker First, Marker Second);

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object[] arguments =
                [
                    new Marker(input + 4),
                    new Marker(input + 8),
                ];
                var pair = Create(arguments);
                return pair.First.Value * 1000 + pair.Second.Value;
            }

            private static Pair Create(object[] arguments) => new(
                (Marker)arguments[0],
                (Marker)arguments[1]);
        }
        """;

    private const string DefaultValueEqualitySource = """
        namespace DefaultValueEquality.Application;

        public readonly struct Nested(int value)
        {
            public int Value { get; } = value;
        }

        public readonly struct Sample(
            int number,
            string text,
            Nested nested,
            long wide,
            nint native)
        {
            public int Number { get; } = number;
            public string Text { get; } = text;
            public Nested Nested { get; } = nested;
            public long Wide { get; } = wide;
            public nint Native { get; } = native;
        }

        public readonly struct Floating(double number, float single)
        {
            public double Number { get; } = number;
            public float Single { get; } = single;
        }

        public readonly struct Generic<T>(T value)
        {
            public T Value { get; } = value;
        }

        public readonly struct Empty;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var left = new Sample(
                    input, new string('x', 1), new Nested(input + 1),
                    input + 2L, input + 3);
                var equal = new Sample(
                    input, new string('x', 1), new Nested(input + 1),
                    input + 2L, input + 3);
                var scalarMismatch = new Sample(
                    input + 1, "x", new Nested(input + 1),
                    input + 2L, input + 3);
                var nestedMismatch = new Sample(
                    input, "x", new Nested(input + 2),
                    input + 2L, input + 3);
                var referenceMismatch = new Sample(
                    input, "y", new Nested(input + 1),
                    input + 2L, input + 3);
                var result = 0;
                if (left.Equals(equal)) result |= 1;
                if (!left.Equals(scalarMismatch)) result |= 2;
                if (!left.Equals(nestedMismatch)) result |= 4;
                if (left.GetHashCode() == equal.GetHashCode()) result |= 8;
                // Unequal values may collide; their hash codes need not differ.
                if (!left.Equals(referenceMismatch)) result |= 16;
                object boxedLeft = left;
                object boxedEqual = equal;
                if (boxedLeft.Equals(boxedEqual)) result |= 32;
                if (boxedLeft.GetHashCode() == boxedEqual.GetHashCode()) result |= 64;
                var floatingLeft = new Floating(0.0, float.NaN);
                var floatingEqual = new Floating(-0.0, float.NaN);
                if (floatingLeft.Equals(floatingEqual) &&
                    floatingLeft.GetHashCode() == floatingEqual.GetHashCode()) result |= 128;
                var alternateNaN = System.BitConverter.Int64BitsToDouble(
                    unchecked((long)0x7ff8000000000001));
                var nanLeft = new Floating(double.NaN, 1);
                var nanEqual = new Floating(alternateNaN, 1);
                if (nanLeft.Equals(nanEqual) &&
                    nanLeft.GetHashCode() == nanEqual.GetHashCode()) result |= 256;
                var genericLeft = new Generic<string>(new string('x', 1));
                var genericEqual = new Generic<string>(new string('x', 1));
                if (genericLeft.Equals(genericEqual)) result |= 512;
                if (genericLeft.GetHashCode() == genericEqual.GetHashCode()) result |= 1024;
                if (new Empty().Equals(new Empty()) &&
                    !boxedLeft.Equals(null) &&
                    !boxedLeft.Equals(new Nested(1))) result |= 2048;
                return result;
            }
        }
        """;
}
