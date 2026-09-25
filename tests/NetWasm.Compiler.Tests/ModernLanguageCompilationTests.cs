using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ModernLanguageCompilationTests
{
    [Fact]
    public void CompilerLowersCSharpFourteenExtensionMembersAndFieldBackedProperties()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "CSharpFourteenFixture",
            """
            namespace CSharpFourteenFixture;

            public static class IntegerExtensions
            {
                extension(int value)
                {
                    public int Increment() => value + 1;
                }
            }

            public sealed class Model
            {
                public int Value
                {
                    get => field;
                    set => field = value;
                }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var model = new Model { Value = input };
                    return model.Value.Increment();
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "CSharpFourteenFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersInitOnlyRequiredMembersAndReadonlyStructs()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ModernLanguageFixture",
            """
            namespace ModernLanguageFixture;

            public sealed class Model
            {
                public required int Value { get; init; }
            }

            public readonly struct Pair
            {
                public Pair(int left, int right)
                {
                    Left = left;
                    Right = right;
                }

                public int Left { get; }
                public int Right { get; }
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var model = new Model { Value = input };
                    var pair = new Pair(model.Value, 1);
                    return pair.Left + pair.Right;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ModernLanguageFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersRecordCopyAndWithExpression()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "RecordFixture",
            """
            namespace RecordFixture;

            public sealed record Model;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new Model();
                    var second = first with { };
                    var equal = new Model();
                    if (!first.Equals(equal)) return 1;
                    if (first != second || object.ReferenceEquals(first, second)) return 2;
                    if (first.GetHashCode() != equal.GetHashCode()) return 3;

                    return input + 1;
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "RecordFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersDefaultInterfacesAndCovariantReturns()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ModernDispatchFixture",
            """
            namespace ModernDispatchFixture;

            public interface IOffset
            {
                int Add(int value) => value + 1;
            }

            public sealed class Offset : IOffset;

            public class Base
            {
                public virtual Base Copy() => new Base();
                public virtual int Value => 0;
            }

            public sealed class Derived : Base
            {
                public override Derived Copy() => new Derived();
                public override int Value => 1;
            }

            public static class EntryPoint
            {
                public static int DefaultInterface(int input)
                {
                    IOffset offset = new Offset();
                    return offset.Add(input);
                }

                public static int CovariantReturn(int input)
                {
                    Base value = new Derived();
                    return input + value.Copy().Value;
                }
            }
            """);

        Assert.Equal(42, CompileAndRun("DefaultInterface"));
        Assert.Equal(42, CompileAndRun("CovariantReturn"));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ModernDispatchFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
        }
    }

    [Fact]
    public void CompilerLowersRefFieldsAndByRefLikeGenericInterfaces()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ModernRefStructFixture",
            """
            namespace ModernRefStructFixture;

            public interface IValue
            {
                int Read();
            }

            public ref struct RefValue : IValue
            {
                private ref int _value;

                public RefValue(ref int value) => _value = ref value;
                public int Read() => _value;
                public void Increment() => _value++;
            }

            public static class EntryPoint
            {
                private static int Read<T>(T value) where T : IValue, allows ref struct =>
                    value.Read();

                public static int Run(int input)
                {
                    var value = input;
                    var reference = new RefValue(ref value);
                    reference.Increment();
                    return Read(reference);
                }
            }
            """);

        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly,
            [assets.CoreLib],
            "ModernRefStructFixture.EntryPoint",
            "Run",
            []));

        Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
    }

    [Fact]
    public void CompilerLowersIndexAndRangeAcrossArraysSpansAndStrings()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "IndexRangeFixture",
            """
            namespace IndexRangeFixture;

            public static class EntryPoint
            {
                public static int ArrayRange(int input)
                {
                    var values = new[] { 7, input, 1, 9 };
                    var middle = values[1..^1];
                    return middle[0] + middle[^1];
                }

                public static int SpanRange(int input)
                {
                    var values = new[] { 7, input, 1, 9 };
                    System.Span<int> span = values;
                    var tail = span[1..];
                    return tail[0] + tail[^1];
                }

                public static int StringRange(int input)
                {
                    var text = "abcd";
                    var inner = text[1..^1];
                    return input + inner.Length - 1;
                }
            }
            """);

        Assert.Equal(42, CompileAndRun("ArrayRange"));
        Assert.Equal(50, CompileAndRun("SpanRange"));
        Assert.Equal(42, CompileAndRun("StringRange"));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "IndexRangeFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
        }
    }

    [Theory]
    [InlineData("ArrayCollection", 42)]
    [InlineData("ParamsCollection", 42)]
    [InlineData("InlineArray", 42)]
    public void CompilerLowersCollectionExpressionsParamsCollectionsAndInlineArrays(
        string method,
        int expected)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource(
            "ModernCollectionFixture",
            """
            namespace ModernCollectionFixture;

            [System.Runtime.CompilerServices.InlineArray(3)]
            public struct Buffer
            {
                private int _element0;
            }

            public static class EntryPoint
            {
                private static int Sum(params System.ReadOnlySpan<int> values)
                {
                    var result = 0;
                    for (var index = 0; index < values.Length; index++)
                    {
                        result += values[index];
                    }
                    return result;
                }

                public static int ArrayCollection(int input)
                {
                    int[] values = [input, 1];
                    return values[0] + values[1];
                }

                public static int ParamsCollection(int input)
                {
                    int[] values = [input, 1];
                    return Sum(values[0], values[1]);
                }

                public static int InlineArray(int input)
                {
                    Buffer values = default;
                    values[0] = input;
                    values[1] = 1;
                    values[2] = 2;
                    return values[0] + values[1] + values[2] - 2;
                }
            }
            """);

        Assert.Equal(expected, CompileAndRun(method));

        int CompileAndRun(string method)
        {
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "ModernCollectionFixture.EntryPoint",
                method,
                []));
            return ExecuteWithNode(result.ApplicationModule, assets.Directory, 41);
        }
    }
}
