using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericMetadataOperandCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesContextualGenericMetadataOperands(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            namespace ContextualGenericMetadataFixture;

            public enum Small : int
            {
                Value = 41,
            }

            public enum Wide : long
            {
                Value = 4294967297L,
            }

            public struct Pair
            {
                public Pair(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Left;

                public int Right;
            }

            public sealed class Marker
            {
                public Marker(int value) => Value = value;

                public int Value;
            }

            public static class TypeOperations<T>
            {
                public static T Stored = default!;

                public static object Box(T value) => value!;

                public static T Unbox(object value) => (T)value;

                public static bool Is(object value) => value is T;

                public static T Echo(T value) => value;
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    object small = BoxMethod(Small.Value);
                    if (!IsMethod<Small>(small) || UnboxMethod<Small>(small) != Small.Value)
                    {
                        return 1;
                    }

                    TypeOperations<Wide>.Stored = Wide.Value;
                    object wide = TypeOperations<Wide>.Box(TypeOperations<Wide>.Stored);
                    if (!TypeOperations<Wide>.Is(wide)
                        || TypeOperations<Wide>.Unbox(wide) != Wide.Value)
                    {
                        return 2;
                    }

                    var pair = new Pair(3, 5);
                    object boxedPair = BoxMethod(pair);
                    var unboxedPair = UnboxMethod<Pair>(boxedPair);
                    if (!IsMethod<Pair>(boxedPair)
                        || unboxedPair.Left != 3
                        || unboxedPair.Right != 5)
                    {
                        return 3;
                    }

                    var marker = new Marker(7);
                    object boxedMarker = TypeOperations<Marker>.Box(marker);
                    if (!TypeOperations<Marker>.Is(boxedMarker)
                        || CastMethod<Marker>(boxedMarker).Value != 7
                        || TypeOperations<Marker>.Echo(marker).Value != 7)
                    {
                        return 4;
                    }

                    return input;
                }

                private static object BoxMethod<T>(T value) => value!;

                private static T UnboxMethod<T>(object value) => (T)value;

                private static bool IsMethod<T>(object value) => value is T;

                private static T CastMethod<T>(object value)
                    where T : class => (T)value;
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "ContextualGenericMetadataFixture",
            source,
            "ContextualGenericMetadataFixture.EntryPoint",
            optimize,
            target,
            17,
            []));

        Assert.Equal(17, observed);
    }
}
