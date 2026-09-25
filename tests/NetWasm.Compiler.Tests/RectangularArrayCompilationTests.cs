using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class RectangularArrayCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesRectangularValueCopiesAndTheirReferences(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "RectangularValueCopiesFixture",
            ValueCopiesSource,
            "RectangularValueCopiesFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(31, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerValidatesRectangularArraysForEveryTargetAndOptimization(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "RectangularArrayWidthFixture",
            WidthSource,
            "RectangularArrayWidthFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, result);
    }

    [Fact]
    public void CompilerExecutesRankGenericRectangularArrayOperations()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RectangularArrayFixture",
            """
            namespace RectangularArrayFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var rank2 = new int[2, 3];
                    rank2[1, 2] = input;
                    ref int reference = ref rank2[1, 2];
                    reference++;

                    var rank3 = new int[2, 2, 2];
                    rank3[1, 0, 1] = 7;
                    bool shape = rank2.Rank == 2
                        && rank2.Length == 6
                        && rank2.LongLength == 6
                        && rank2.GetLength(0) == 2
                        && rank2.GetLongLength(1) == 3
                        && rank2.GetLowerBound(1) == 0
                        && rank2.GetUpperBound(1) == 2
                        && rank3.Rank == 3
                        && rank3.GetLength(2) == 2;
                    return shape ? rank2[1, 2] + rank3[1, 0, 1] : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RectangularArrayFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(49, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerPreservesRectangularArrayShapeAndExceptionSemantics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RectangularArrayFailuresFixture",
            """
            namespace RectangularArrayFailuresFixture;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    int passed = 0;
                    var empty = new int[0, 3];
                    if (empty.Rank == 2 && empty.Length == 0
                        && empty.GetLength(0) == 0 && empty.GetLength(1) == 3)
                        passed++;
                    try { _ = new int[input - 42, 1]; }
                    catch (System.OverflowException) { passed++; }
                    try { _ = new int[int.MaxValue, 2]; }
                    catch (System.OutOfMemoryException) { passed++; }
                    try { _ = empty.GetLength(2); }
                    catch (System.IndexOutOfRangeException) { passed++; }
                    try { _ = empty[0, 0]; }
                    catch (System.IndexOutOfRangeException) { passed++; }
                    try
                    {
                        int[,] missing = null;
                        _ = missing[0, 0];
                    }
                    catch (System.NullReferenceException) { passed++; }
                    return passed;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RectangularArrayFailuresFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(6, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerExecutesRectangularArraysAcrossElementFamiliesAndGenerics()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RectangularArrayFamiliesFixture",
            """
            namespace RectangularArrayFamiliesFixture;

            public struct Pair
            {
                public string Text;
                public int Value;
            }

            public static class EntryPoint
            {
                private static T RoundTrip<T>(T value)
                {
                    var values = new T[2, 2];
                    values[1, 0] = value;
                    return values[1, 0];
                }

                public static int Run(int input)
                {
                    var references = new string[2, 2];
                    references[0, 1] = "ok";
                    var pairs = new Pair[2, 2];
                    pairs[1, 1] = new Pair { Text = "pair", Value = input };
                    var nullable = new int?[1, 2];
                    nullable[0, 1] = input;
                    var rank4 = new int[1, 1, 1, 2];
                    rank4[0, 0, 0, 1] = 7;
                    return references[0, 1] == "ok"
                        && pairs[1, 1].Text == "pair"
                        && pairs[1, 1].Value == input
                        && nullable[0, 1].Value == input
                        && RoundTrip(input) == input
                        && RoundTrip("generic") == "generic"
                        && rank4.Rank == 4
                        && rank4[0, 0, 0, 1] == 7
                        ? input + 1
                        : -1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RectangularArrayFamiliesFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    private const string ValueCopiesSource = """
        using System;
        namespace RectangularValueCopiesFixture;

        public sealed class Box { public int Value; }
        public struct Pair { public Box Reference; public long Number; }
        public struct Envelope<T> { public T Value; public long Marker; }

        public static class EntryPoint
        {
            private static T Read<T>(T[,] values) => values[1, 1];
            private static Pair ReadPair(Pair[,] values) => values[1, 1];
            private static Pair ReadCube(Pair[,,] values) => values[1, 0, 1];

            private static bool CheckCopies(Pair first, Pair second, int input)
            {
                GC.Collect();
                return first.Reference.Value == input && first.Number == 10000000001L
                    && second.Reference.Value == input + 1 && second.Number == 20000000002L;
            }

            public static int Run(int input)
            {
                var values = new Pair[2, 2];
                values[1, 1] = new Pair
                {
                    Reference = new Box { Value = input }, Number = 10000000001L
                };
                values[0, 0] = new Pair
                {
                    Reference = new Box { Value = input + 1 }, Number = 20000000002L
                };
                var direct = ReadPair(values);
                var generic = Read(values);
                var passed = CheckCopies(values[1, 1], values[0, 0], input) ? 1 : 0;
                values[1, 1] = default;
                GC.Collect();
                if (direct.Reference.Value == input && direct.Number == 10000000001L)
                    passed |= 2;
                generic.Number = -1;
                if (generic.Reference.Value == input && direct.Number == 10000000001L)
                    passed |= 4;

                var cube = new Pair[2, 1, 2];
                cube[1, 0, 1] = direct;
                var cubeCopy = ReadCube(cube);
                cube[1, 0, 1] = default;
                GC.Collect();
                if (cubeCopy.Reference.Value == input && cubeCopy.Number == 10000000001L)
                    passed |= 8;

                var envelopes = new Envelope<Pair>[2, 2];
                envelopes[1, 1] = new Envelope<Pair> { Value = direct, Marker = 73 };
                var envelope = Read(envelopes);
                envelopes[1, 1] = default;
                var nullable = new int?[2, 2];
                nullable[1, 1] = input;
                var optional = Read(nullable);
                nullable[1, 1] = null;
                GC.Collect();
                if (envelope.Value.Reference.Value == input && envelope.Marker == 73
                    && optional.HasValue && optional.Value == input)
                    passed |= 16;
                return passed;
            }
        }
        """;

    private const string WidthSource = """
        namespace RectangularArrayWidthFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var values = new long[2, 3];
                values[1, 2] = input;
                return values.Rank == 2 && values.GetLength(1) == 3
                    ? (int)values[1, 2] + 1
                    : -1;
            }
        }
        """;
}
