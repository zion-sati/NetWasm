namespace NetWasm.Compiler.Tests.Correctness;

// Issue 228: a static virtual member called through a type parameter resolves on the exact type
// argument, sealed or not. Each program runs as desktop .NET does: the issue's static abstract
// factory on an unsealed class, and its siblings, which reach the member from a generic type, through
// a delegate, as an operator and as a generic method, on a class with a derived class, on a generic
// class, and through a default implementation an unsealed class does not override.
public sealed class StaticVirtualTypeArgumentCompilationTests(CorrectnessTestRunner runner)
{
    public static TheoryData<string> Cases => [.. Programs.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void AStaticVirtualResolvesOnTheExactTypeArgument(string name) =>
        runner.Run(new("StaticVirtual" + name, "StaticVirtuals", Prelude + Programs[name], [0, 3, 7])
        {
            ReportAllMismatches = true,
        });

    private const string Prelude = """
        using System;
        namespace StaticVirtuals;

        public static partial class EntryPoint
        {
            public static int Trace() => 0;
        }

        public interface IFactory<TSelf> where TSelf : IFactory<TSelf>
        {
            static abstract TSelf Create(string label);
            static virtual string Kind() => "factory";
        }

        public class Box(string label) : IFactory<Box>
        {
            public string Label => label;
            public static Box Create(string label) => new(label);
        }

        public class Crate(string label) : Box(label) { }

        public class Pair<T>(string label) : IFactory<Pair<T>>
        {
            public string Label => label + typeof(T).IsValueType;
            public static Pair<T> Create(string label) => new(label);
            public static string Kind() => "pair";
        }

        """;

    private static readonly Dictionary<string, string> Programs = new()
    {
        // The issue's program: a static abstract factory on an unsealed class.
        ["Factory"] = """
            public static partial class EntryPoint
            {
                private static T Make<T>(string label) where T : IFactory<T> => T.Create(label);

                public static int Run(int input) => Make<Box>("made" + input).Label.Length * 10 + input;
            }
            """,
        // The class has a derived class, and the call comes from a generic type.
        ["FromAGenericType"] = """
            public static class Maker<T> where T : IFactory<T>
            {
                public static T Make(string label) => T.Create(label);
                public static string Kind() => T.Kind();
            }

            public static partial class EntryPoint
            {
                public static int Run(int input)
                {
                    Box crate = new Crate("crate");
                    return Maker<Box>.Make("box" + input).Label.Length * 100 + crate.Label.Length * 10 +
                        Maker<Box>.Kind().Length + Maker<Pair<int>>.Kind().Length * 1000;
                }
            }
            """,
        // A delegate to the static abstract, and a generic class as the type argument.
        ["Delegate"] = """
            public static partial class EntryPoint
            {
                private static Func<string, T> Factory<T>() where T : IFactory<T> => T.Create;

                public static int Run(int input) =>
                    Factory<Box>()("d" + input).Label.Length * 100 + Factory<Pair<string>>()("p").Label.Length;
            }
            """,
        // A static abstract operator and a static abstract generic method on an unsealed class.
        ["OperatorAndGenericMethod"] = """
            public interface IMeasure<TSelf> where TSelf : IMeasure<TSelf>
            {
                static abstract TSelf operator +(TSelf left, TSelf right);
                static abstract TSelf From<TValue>(TValue value);
                int Value { get; }
            }

            public class Length(int value) : IMeasure<Length>
            {
                public int Value => value;
                public static Length operator +(Length left, Length right) => new(left.Value + right.Value);
                public static Length From<TValue>(TValue value) => new(value?.ToString()?.Length ?? 0);
            }

            public static partial class EntryPoint
            {
                private static T Sum<T>(T left, T right) where T : IMeasure<T> => left + right;
                private static T From<T, TValue>(TValue value) where T : IMeasure<T> => T.From(value);

                public static int Run(int input) =>
                    Sum(new Length(input), new Length(5)).Value * 100 + From<Length, string>("four").Value * 10 +
                    From<Length, int>(input * 100).Value;
            }
            """,
    };
}
