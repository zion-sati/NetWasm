using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class LanguageSurfaceCompilationTests
{
    [Fact]
    public void CompilerExecutesDeclarationsOperatorsEventsAndParameterModifiers()
    {
        using var assets = TestAssets.Create();
        const string source = """
            #nullable enable
            using Alias = LanguageSurfaceFixture.Measure;

            namespace LanguageSurfaceFixture;

            file static class SourceConstants
            {
                internal const int One = 1;
            }

            public interface IReader
            {
                int Read();
            }

            public abstract class ReaderBase : IReader
            {
                protected internal readonly int Value;
                protected ReaderBase(int value) => Value = value;
                public abstract int Read();
                protected virtual int Offset() => SourceConstants.One;
            }

            public sealed class Reader(int value) : ReaderBase(value)
            {
                public override int Read() => Value + Offset();
            }

            public readonly struct Measure
            {
                private readonly int _value;
                public Measure(int value) => _value = value;
                public static implicit operator Measure(int value) => new(value);
                public static explicit operator int(Measure value) => value._value;
                public static Measure operator +(Measure left, Measure right) =>
                    new(left._value + right._value);
            }

            public static class EntryPoint
            {
                private static volatile int _observed;
                private static event System.Action<int>? Changed;

                private static int Mutate(
                    ref int value,
                    out int doubled,
                    in int delta,
                    params int[] extras)
                {
                    value += delta;
                    doubled = value * 2;
                    foreach (var extra in extras)
                    {
                        value += extra;
                    }
                    return value;
                }

                public static int Run(int input)
                {
                    var delta = 0;
                    var value = input;
                    var mutated = Mutate(ref value, out var doubled, in delta);
                    Changed += static observed => _observed = observed;
                    Changed!(mutated);
                    Changed -= static observed => _observed = observed;
                    IReader reader = new Reader(_observed);
                    Alias measure = reader.Read();
                    measure += (Alias)(-1);
                    return (int)measure == input && doubled == input * 2
                        && nameof(EntryPoint) == "EntryPoint"
                        && typeof(Reader) == new global::LanguageSurfaceFixture.Reader(0).GetType()
                        ? input + SourceConstants.One
                        : -1;
                }
            }
            """;

        foreach (var optimize in new[] { false, true })
        {
            var assembly = optimize
                ? assets.CompileOptimizedSource("LanguageSurfaceOptimized", source)
                : assets.CompileSource("LanguageSurfaceDebug", source);
            var result = NetWasmCompiler.Compile(new CompilerOptions(
                assembly,
                [assets.CoreLib],
                "LanguageSurfaceFixture.EntryPoint",
                "Run",
                []));

            Assert.Equal(42, ExecuteWithNode(result.ApplicationModule, assets.Directory, 41));
        }
    }
}
