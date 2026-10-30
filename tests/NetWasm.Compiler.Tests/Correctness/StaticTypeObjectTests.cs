namespace NetWasm.Compiler.Tests.Correctness;

// typeof uses a static Type object that the runtime adopts at startup as its
// type's object, so it must be the very object GetType returns: across generic
// instantiations, as a dictionary key or a lock, after a collection, and where
// two typeof branches meet and the type is only known at run time. GetType on a
// receiver of one possible type uses that object too, and still throws on null.
[Collection(CorrectnessTestGroup.Name)]
public sealed class StaticTypeObjectTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void TypeofIsTheObjectGetTypeReturns() =>
        runner.Run(CreateFixture().WithFullExecutionMatrix());

    [Fact]
    public void TypeofIsTheObjectGetTypeReturnsInTheLinkedRuntime() =>
        runner.Run(CreateFixture() with
        {
            Matrix = new(CorpusMatrixProfile.Extended, "Release-Wasm32-Direct-Linked", CorpusExecutionBackend.Linked),
        });

    // GetType on a receiver of unknown type reads the runtime's Type object
    // cache before asking the runtime: while anything holds a type's object
    // every GetType returns it, and once a collection frees it the next one is
    // an equal object. None of these types is named by typeof, so no static
    // object stands in for them.
    [Fact]
    public void GetTypeOnUnknownReceiversReturnsTheRuntimesObjectInTheLinkedRuntime() =>
        runner.Run(new CorpusFixture(
            "DynamicTypeObjects",
            "NetWasm.Correctness.DynamicTypeObjects",
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.DynamicTypeObjects;

            public sealed class Node;

            public sealed class Pair<T>(T first)
            {
                public T First = first;
            }

            public static class EntryPoint
            {
                private static object[] s_values = [];

                public static int Run(int input)
                {
                    s_values =
                    [
                        new Node(), new Pair<int>(input), new Pair<Node>(new Node()), new Node[2],
                        new List<long>(), (input & 1) == 0 ? "even" : (object)input,
                    ];
                    var hash = 17;
                    var held = new Type[s_values.Length];
                    for (var index = 0; index < s_values.Length; index++)
                    {
                        held[index] = s_values[index].GetType();
                    }
                    GC.Collect();
                    for (var round = 0; round < 3; round++)
                    {
                        for (var index = 0; index < s_values.Length; index++)
                        {
                            var type = s_values[index].GetType();
                            hash = Mix(hash, ReferenceEquals(type, held[index]));
                            hash = Mix(hash, type == held[index] && type.Equals(held[index]));
                            hash = Mix(hash, type != held[(index + 1) % held.Length]);
                        }
                        GC.Collect();
                    }
                    var before = Hash(s_values[1]);
                    GC.KeepAlive(held);
                    held = [];
                    GC.Collect();
                    GC.Collect();
                    hash = Mix(hash, Hash(s_values[1]) == before);
                    hash = Mix(hash, Same(s_values[2], new Pair<Node>(new Node())));
                    hash = Mix(hash, !Same(s_values[0], s_values[3]));
                    return hash;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Hash(object value) => value.GetType().GetHashCode();

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static bool Same(object left, object right) => left.GetType() == right.GetType();

                private static int Mix(int hash, bool value) => unchecked(hash * 31 + (value ? 1 : 0));

                public static int Trace() => 0;
            }
            """,
            [0, 1, 5])
        {
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection |
                OracleRuntimeCapabilities.WeakReferenceClearing,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private static CorpusFixture CreateFixture() => new(
        "StaticTypeObjects",
        "NetWasm.Correctness.StaticTypeObjects",
        """
        using System;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.StaticTypeObjects;

        public enum Shade { Light, Dark }

        public sealed class Box<T>(T value)
        {
            public T Value { get; } = value;
        }

        public sealed class Leaf;

        public static class EntryPoint
        {
            private static string? s_text = "text";
            private static string? s_missing = null;

            public static int Run(int input)
            {
                var hash = 17;
                hash = Mix(hash, s_text!.GetType() == typeof(string));
                hash = Mix(hash, "literal".GetType() == typeof(string));
                hash = Mix(hash, new Leaf().GetType() == typeof(Leaf));
                hash = Mix(hash, new Box<int>(input).GetType() == typeof(Box<int>));
                hash = Mix(hash, ((object)Shade.Dark).GetType() == typeof(Shade));
                hash = Mix(hash, ((object)input).GetType() == typeof(int));
                hash = Mix(hash, ((object)(input * 0.5)).GetType() == typeof(double));
                int? maybe = (input & 1) == 0 ? input : null;
                if (maybe.HasValue)
                {
                    hash = Mix(hash, ((object)maybe).GetType() == typeof(int));
                }
                try
                {
                    hash = Mix(hash, s_missing!.GetType() == typeof(string));
                }
                catch (NullReferenceException)
                {
                    hash = unchecked(hash * 31 + 7);
                }
                Leaf? none = (input & 2) == 0 ? null : new Leaf();
                try
                {
                    hash = Mix(hash, none!.GetType() == typeof(Leaf));
                }
                catch (NullReferenceException)
                {
                    hash = unchecked(hash * 31 + 9);
                }
                hash = Mix(hash, typeof(string) == "text".GetType());
                hash = Mix(hash, ReferenceEquals(typeof(int), ((object)input).GetType()));
                hash = Mix(hash, typeof(List<int>) == new List<int>().GetType());
                hash = Mix(hash, typeof(int[]) == new int[input].GetType());
                hash = Mix(hash, typeof(Shade) == ((object)(Shade)(input & 1)).GetType());
                hash = Mix(hash, typeof(Box<string>) == new Box<string>("x").GetType());
                hash = Mix(hash, typeof(Box<int>) == new Box<string>("y").GetType());
                hash = Mix(hash, Matches(input));
                hash = Mix(hash, Matches("text"));
                hash = Mix(hash, Matches(new Box<long>(input)));
                hash = Mix(hash, Matches<object>("text"));

                var kinds = new Dictionary<Type, int>
                {
                    [typeof(string)] = 1,
                    [typeof(int)] = 2,
                    [typeof(List<int>)] = 3,
                    [typeof(Box<string>)] = 4,
                };
                object[] values = ["text", input, new List<int>(), new Box<string>("z"), 2.5, (Shade)input];
                foreach (var value in values)
                {
                    hash = unchecked(hash * 31 + (kinds.TryGetValue(value.GetType(), out var kind) ? kind : -1));
                }

                GC.Collect();
                hash = Mix(hash, typeof(string) == "after".GetType());
                hash = Mix(hash, typeof(string).GetHashCode() == "after".GetType().GetHashCode());

                // Both branches reach one materialization, which stays dynamic.
                var chosen = (input & 1) == 0 ? typeof(int) : typeof(long);
                hash = Mix(hash, chosen == ((input & 1) == 0 ? (object)input : (object)(long)input).GetType());

                lock (typeof(EntryPoint))
                {
                    hash = unchecked(hash * 31 + 5);
                }
                return hash;
            }

            private static bool Matches<T>(T value) => typeof(T) == value!.GetType();

            private static int Mix(int hash, bool value) => unchecked(hash * 31 + (value ? 1 : 0));

            public static int Trace() => 0;
        }
        """,
        [0, 1, 2, 7]);
}
